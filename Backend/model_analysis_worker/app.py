import io
import json
import os
import re
import tempfile
import time
import zipfile
from pathlib import Path
from uuid import UUID

import requests
from fastapi import FastAPI, HTTPException
from fastapi.responses import Response
from google import genai
from google.genai import types
from PIL import Image
from pydantic import BaseModel

from glb_analyzer import (
    analyze_glb,
    render_points_cpu,
    normalized_point_to_3d_anchor,
)


APP_VERSION = "1.18.0"

GEMINI_MODELS = [
    "gemini-3.5-flash-lite",
    "gemini-3.6-flash",
]

# Keep every inference bounded.
# Primary model is the low-latency Gemini 3.5 Flash-Lite.
# Fallback gets a slightly longer timeout.
GEMINI_TIMEOUT_MS = 30000
GEMINI_FALLBACK_TIMEOUT_MS = 45000

# Sixteen multi-angle views improve anatomical coverage in one contact-sheet request.
IDENTIFY_VIEWS = ["front", "front_right", "right", "back_right", "back", "back_left", "left", "front_left", "top", "bottom", "top_front", "top_right", "top_back", "top_left", "bottom_front", "bottom_back"]
IDENTIFY_IMAGE_SIZE = 320
IDENTIFY_MAX_POINTS = 24000
IDENTIFY_MAX_OUTPUT_TOKENS = 6000

# Two-stage semantic pipeline:
# Stage 1 focuses only on detecting/localizing as many supported parts as possible.
# Stage 2 writes detailed Vietnamese educational content in smaller batches.
DETECTION_MAX_OUTPUT_TOKENS = 3000
DESCRIPTION_BATCH_SIZE = 5
IDENTIFY_MIN_CONFIDENCE = 0.65

# Targeted high-detail pass for thin coronary vessels.
# Kept below the previous 512px/50k configuration to avoid Cloud Run
# memory pressure while still preserving more detail than the general pass.
CORONARY_IMAGE_SIZE = 384
CORONARY_MAX_POINTS = 30000
CORONARY_MIN_CONFIDENCE = 0.50

# Final refinement stage for coronary structures that remain missing after the
# normal multi-pass detector. The refinement stage first proposes candidates,
# then a second Gemini call verifies them before they are merged.
CORONARY_REFINEMENT_MIN_CONFIDENCE = 0.45
CORONARY_VERIFIED_MIN_CONFIDENCE = 0.55
CORONARY_REFINEMENT_MAX_OUTPUT_TOKENS = 2200

CORONARY_REFINEMENT_TARGETS = [
    {
        "part_name_en": "Right Coronary Artery",
        "part_name_vi": "Động mạch vành phải",
    },
    {
        "part_name_en": "Left Coronary Artery",
        "part_name_vi": "Động mạch vành trái",
    },
    {
        "part_name_en": "Circumflex Artery",
        "part_name_vi": "Nhánh mũ của động mạch vành trái",
    },
    {
        "part_name_en": "Great Cardiac Vein",
        "part_name_vi": "Tĩnh mạch tim lớn",
    },
    {
        "part_name_en": "Small Cardiac Vein",
        "part_name_vi": "Tĩnh mạch tim nhỏ",
    },
    {
        "part_name_en": "Middle Cardiac Vein",
        "part_name_vi": "Tĩnh mạch tim giữa",
    },
    {
        "part_name_en": "Coronary Sinus",
        "part_name_vi": "Xoang vành",
    },
    {
        "part_name_en": "Posterior Interventricular Artery",
        "part_name_vi": "Động mạch gian thất sau",
    },
]
CORONARY_VIEWS = [
    "front",
    "front_right",
    "right",
    "back_right",
    "back",
    "back_left",
    "left",
    "front_left",
]



app = FastAPI(
    title="3D Model Analysis Worker",
    version=APP_VERSION,
)


class AnalyzeRequest(BaseModel):
    model_url: str


class RenderRequest(BaseModel):
    model_url: str
    view: str = "front"


class IdentifyPartsRequest(BaseModel):
    asset_id: UUID
    model_url: str


@app.get("/health")
def health():
    return {
        "success": True,
        "service": "3d-model-analysis-worker",
        "version": APP_VERSION,
    }


def download_model(model_url: str, destination: Path):
    if not model_url or not model_url.strip():
        raise HTTPException(
            status_code=400,
            detail="model_url is required.",
        )

    response = requests.get(model_url, timeout=120)
    response.raise_for_status()

    if not response.content:
        raise ValueError("Downloaded model is empty.")

    destination.write_bytes(response.content)


def get_gemini_api_key() -> str:
    api_key = os.environ.get("GEMINI_API_KEY", "").strip()

    if not api_key:
        raise HTTPException(
            status_code=500,
            detail="GEMINI_API_KEY is missing.",
        )

    return api_key


def is_retryable_gemini_error(exc: Exception) -> bool:
    error_text = str(exc).upper()

    retryable_markers = (
        "429",
        "503",
        "RESOURCE_EXHAUSTED",
        "UNAVAILABLE",
        "RATE_LIMIT",
        "RATE LIMIT",
        "HIGH DEMAND",
        "TEMPORARILY",
    )

    return any(
        marker in error_text
        for marker in retryable_markers
    )


def call_gemini_with_retry(contents, max_output_tokens=None, stage="identify"):
    """Fast bounded Gemini inference with one fallback model.

    Primary: gemini-3.5-flash-lite.
    Fallback: gemini-3.6-flash.
    """
    api_key = get_gemini_api_key()
    last_error = None

    for index, model_name in enumerate(GEMINI_MODELS):
        timeout_ms = (
            GEMINI_TIMEOUT_MS
            if index == 0
            else GEMINI_FALLBACK_TIMEOUT_MS
        )
        started = time.monotonic()
        client = None

        try:
            print(
                f"[Gemini:{stage}] model={model_name} timeout={timeout_ms}ms",
                flush=True,
            )

            client = genai.Client(
                api_key=api_key,
                http_options=types.HttpOptions(timeout=timeout_ms),
            )

            response = client.models.generate_content(
                model=model_name,
                contents=contents,
                config=types.GenerateContentConfig(
                    max_output_tokens=(
                        max_output_tokens
                        if max_output_tokens is not None
                        else IDENTIFY_MAX_OUTPUT_TOKENS
                    ),
                    response_mime_type="application/json",
                ),
            )

            elapsed = round(time.monotonic() - started, 2)

            if response is None:
                raise RuntimeError(
                    "Gemini returned an empty response object."
                )

            print(
                f"[Gemini:{stage}] success model={model_name} elapsed={elapsed}s",
                flush=True,
            )
            return response, model_name, 1, elapsed

        except Exception as exc:
            last_error = exc
            elapsed = round(time.monotonic() - started, 2)
            print(
                f"[Gemini:{stage}] failed model={model_name} elapsed={elapsed}s: {exc}",
                flush=True,
            )

            if index < len(GEMINI_MODELS) - 1:
                print(
                    f"[Gemini] falling back to {GEMINI_MODELS[index + 1]}",
                    flush=True,
                )

        finally:
            if client is not None:
                try:
                    client.close()
                except Exception:
                    pass

    raise HTTPException(
        status_code=503,
        detail={
            "message": "Gemini inference failed on primary and fallback models.",
            "models_tried": GEMINI_MODELS,
            "error": str(last_error),
        },
    )


def clean_json_response(raw_text: str) -> str:
    text = (raw_text or "").strip()

    if text.startswith("```json"):
        text = text[len("```json"):].strip()
    elif text.startswith("```"):
        text = text[len("```"):].strip()

    if text.endswith("```"):
        text = text[:-3].strip()

    return text


def get_supabase_config():
    supabase_url = (
        os.environ.get("SUPABASE_URL", "")
        .strip()
        .rstrip("/")
    )

    supabase_secret_key = (
        os.environ.get("SUPABASE_SECRET_KEY", "")
        .strip()
    )

    if not supabase_url:
        raise HTTPException(
            status_code=500,
            detail="SUPABASE_URL is missing.",
        )

    if not supabase_secret_key:
        raise HTTPException(
            status_code=500,
            detail="SUPABASE_SECRET_KEY is missing.",
        )

    return supabase_url, supabase_secret_key


def get_supabase_headers():
    _, supabase_secret_key = get_supabase_config()

    return {
        "apikey": supabase_secret_key,
        "Accept": "application/json",
        "Content-Type": "application/json",
    }


def make_part_key(part_name: str) -> str:
    """
    Convert an AI part name into snake_case.
    Example: "Left Ventricle" -> "left_ventricle"
    """
    value = part_name.strip().lower()
    value = re.sub(r"[^a-z0-9]+", "_", value)
    value = value.strip("_")

    if not value:
        raise ValueError(
            f"Could not create part_key from part_name: {part_name!r}"
        )

    return value


def deactivate_existing_ai_parts(asset_id: UUID) -> int:
    """
    Mark previously generated AI parts for this asset as inactive before
    saving the latest Gemini result set.

    Only rows with source='ai' are touched so manually managed rows are not
    changed. Unity can then safely read only is_active=true rows.
    """
    supabase_url, _ = get_supabase_config()

    headers = get_supabase_headers()
    headers["Prefer"] = "return=representation"

    response = requests.patch(
        f"{supabase_url}/rest/v1/model_parts",
        headers=headers,
        params={
            "asset_id": f"eq.{asset_id}",
            "source": "eq.ai",
            "is_active": "eq.true",
        },
        json={
            "is_active": False,
        },
        timeout=30,
    )

    if not response.ok:
        raise HTTPException(
            status_code=502,
            detail={
                "message": "Failed to deactivate previous AI model_parts.",
                "supabase_status": response.status_code,
                "supabase_response": response.text,
            },
        )

    if not response.content:
        return 0

    try:
        rows = response.json()
    except ValueError:
        return 0

    return len(rows) if isinstance(rows, list) else 0


def upsert_model_parts(
    asset_id: UUID,
    parts: list,
    model_used: str,
):
    """
    Upsert AI semantic parts and calculated anchors into public.model_parts.

    Conflict target:
        (asset_id, part_key)

    The AI-generated description, structure_description, and
    function_description are updated together on conflict.
    """
    if not parts:
        return []

    supabase_url, _ = get_supabase_config()
    payload = []

    for display_order, part in enumerate(parts):
        part_name_en = str(
            part.get("part_name_en", part.get("part_name", ""))
        ).strip()

        part_name_vi = str(
            part.get("part_name_vi", part.get("part_name", ""))
        ).strip()

        if not part_name_en or not part_name_vi:
            continue

        # IMPORTANT:
        # part_key stays based on the canonical English name so repeated
        # generations keep a stable database key even though the UI name is
        # Vietnamese and may contain diacritics.
        part_key = make_part_key(part_name_en)

        confidence = float(part.get("confidence", 0.0))
        confidence = max(0.0, min(1.0, confidence))

        anchor_debug = part.get("anchor_debug", {})

        anchor_metadata = {
            "normalized_x": part.get("normalized_x"),
            "normalized_y": part.get("normalized_y"),
            "pixel_x": anchor_debug.get("pixel_x"),
            "pixel_y": anchor_debug.get("pixel_y"),
            "matched_pixel_x": anchor_debug.get("matched_pixel_x"),
            "matched_pixel_y": anchor_debug.get("matched_pixel_y"),
            "pixel_distance": anchor_debug.get("pixel_distance"),
            "normalized_depth": anchor_debug.get("normalized_depth"),
            "model_used": model_used,
            "method": "vision_raycast",
            "part_name_en": part_name_en,
        }

        payload.append({
            "asset_id": str(asset_id),
            "part_key": part_key,
            "part_name": part_name_vi,
            "description": str(part.get("description", "")).strip(),
            "structure_description": str(part.get("structure_description", part.get("structure", ""))).strip(),
            "function_description": str(part.get("function", "")).strip(),
            "anchor_x": part.get("anchor_x"),
            "anchor_y": part.get("anchor_y"),
            "anchor_z": part.get("anchor_z"),
            "display_order": display_order,
            "source": "ai",
            "is_verified": False,
            "is_active": True,
            "ai_confidence": confidence,
            "anchor_source": "vision_raycast",
            "anchor_confidence": confidence,
            "anchor_view": part.get("best_view"),
            "anchor_metadata": anchor_metadata,
        })

    if not payload:
        return []

    headers = get_supabase_headers()
    headers["Prefer"] = "resolution=merge-duplicates,return=representation"

    response = requests.post(
        f"{supabase_url}/rest/v1/model_parts",
        headers=headers,
        params={"on_conflict": "asset_id,part_key"},
        json=payload,
        timeout=30,
    )

    if not response.ok:
        raise HTTPException(
            status_code=502,
            detail={
                "message": "Failed to upsert model_parts into Supabase.",
                "supabase_status": response.status_code,
                "supabase_response": response.text,
            },
        )

    if not response.content:
        return []

    try:
        return response.json()
    except ValueError:
        return []


@app.get("/supabase-test")
def supabase_test():
    try:
        supabase_url, _ = get_supabase_config()

        response = requests.get(
            f"{supabase_url}/rest/v1/model_parts",
            headers=get_supabase_headers(),
            params={
                "select": "*",
                "limit": "1",
            },
            timeout=30,
        )

        response.raise_for_status()
        rows = response.json()

        return {
            "success": True,
            "message": "Supabase connection OK",
            "model_parts_sample_count": (
                len(rows)
                if isinstance(rows, list)
                else 0
            ),
        }

    except HTTPException:
        raise

    except requests.RequestException as exc:
        detail = str(exc)

        if getattr(exc, "response", None) is not None:
            try:
                detail = exc.response.text
            except Exception:
                pass

        raise HTTPException(
            status_code=502,
            detail=f"Supabase request failed: {detail}",
        )

    except Exception as exc:
        raise HTTPException(
            status_code=500,
            detail=str(exc),
        )


@app.get("/gemini-test")
def gemini_test():
    """Fast auth/connectivity check without generation."""
    client = None
    try:
        client = genai.Client(
            api_key=get_gemini_api_key(),
            http_options=types.HttpOptions(timeout=10000),
        )
        model = client.models.get(model=GEMINI_MODELS[0])
        return {
            "success": True,
            "message": "Gemini API connection OK",
            "model": getattr(model, "name", None) or GEMINI_MODELS[0],
            "mode": "metadata_check",
        }
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=503, detail=str(exc))
    finally:
        if client is not None:
            try:
                client.close()
            except Exception:
                pass


@app.post("/analyze")
def analyze_model(request: AnalyzeRequest):
    try:
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            glb_path = temp_path / "model.glb"

            download_model(
                request.model_url,
                glb_path,
            )

            analysis = analyze_glb(str(glb_path))

            return {
                "success": True,
                "analysis": analysis,
            }

    except requests.RequestException as exc:
        raise HTTPException(
            status_code=502,
            detail=(
                "Failed to download GLB model: "
                f"{exc}"
            ),
        )

    except HTTPException:
        raise

    except Exception as exc:
        raise HTTPException(
            status_code=500,
            detail=str(exc),
        )


@app.post("/render")
def render_model(request: RenderRequest):
    allowed_views = set(IDENTIFY_VIEWS)

    selected_view = request.view.strip().lower()

    if selected_view not in allowed_views:
        raise HTTPException(
            status_code=400,
            detail=(
                "Invalid view. Allowed values: "
                + ", ".join(IDENTIFY_VIEWS)
            ),
        )

    try:
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)

            glb_path = temp_path / "model.glb"
            render_path = temp_path / f"{selected_view}.png"

            download_model(
                request.model_url,
                glb_path,
            )

            render_points_cpu(
                file_path=str(glb_path),
                output_path=str(render_path),
                view=selected_view,
                image_size=384,
                max_points=30000,
            )

            if not render_path.exists():
                raise RuntimeError(
                    "Rendered image was not created."
                )

            return Response(
                content=render_path.read_bytes(),
                media_type="image/png",
                headers={
                    "Content-Disposition": (
                        "inline; "
                        f'filename="{selected_view}.png"'
                    )
                },
            )

    except requests.RequestException as exc:
        raise HTTPException(
            status_code=502,
            detail=(
                "Failed to download GLB model: "
                f"{exc}"
            ),
        )

    except HTTPException:
        raise

    except Exception as exc:
        raise HTTPException(
            status_code=500,
            detail=str(exc),
        )


@app.post("/render-all")
def render_all_views(request: AnalyzeRequest):
    # Export the same 16 views used by the semantic analysis pipeline.
    views = list(IDENTIFY_VIEWS)

    try:
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            glb_path = temp_path / "model.glb"

            download_model(
                request.model_url,
                glb_path,
            )

            rendered_files = []

            for view in views:
                render_path = temp_path / f"{view}.png"

                render_points_cpu(
                    file_path=str(glb_path),
                    output_path=str(render_path),
                    view=view,
                    image_size=320,
                    max_points=20000,
                )

                if not render_path.exists():
                    raise RuntimeError(
                        f"Rendered image was not created: {view}"
                    )

                rendered_files.append(
                    (view, render_path)
                )

            zip_buffer = io.BytesIO()

            with zipfile.ZipFile(
                zip_buffer,
                mode="w",
                compression=zipfile.ZIP_DEFLATED,
            ) as zip_file:
                for view, render_path in rendered_files:
                    zip_file.write(
                        render_path,
                        arcname=f"{view}.png",
                    )

            zip_buffer.seek(0)

            return Response(
                content=zip_buffer.getvalue(),
                media_type="application/zip",
                headers={
                    "Content-Disposition": (
                        "attachment; "
                        'filename="model_views.zip"'
                    )
                },
            )

    except requests.RequestException as exc:
        raise HTTPException(
            status_code=502,
            detail=(
                "Failed to download GLB model: "
                f"{exc}"
            ),
        )

    except HTTPException:
        raise

    except Exception as exc:
        raise HTTPException(
            status_code=500,
            detail=str(exc),
        )



def extract_parts_list(payload, stage_name: str):
    """
    Gemini normally returns:
        {"parts": [...]}

    In practice, a valid JSON top-level array may occasionally be returned:
        [...]

    Accept both forms so the API does not crash with:
        'list' object has no attribute 'get'
    """
    if isinstance(payload, list):
        return payload

    if isinstance(payload, dict):
        parts = payload.get("parts", [])

        if isinstance(parts, list):
            return parts

        raise HTTPException(
            status_code=502,
            detail={
                "message": f"{stage_name} JSON field 'parts' is not a list.",
                "payload_type": type(parts).__name__,
            },
        )

    raise HTTPException(
        status_code=502,
        detail={
            "message": f"{stage_name} returned an unsupported JSON shape.",
            "payload_type": type(payload).__name__,
        },
    )


@app.post("/identify-parts")
def identify_parts(request: IdentifyPartsRequest):
    """
    Two-stage semantic analysis pipeline:

      1) download and analyze the GLB,
      2) render sixteen multi-angle views,
      3) combine them into one 4x4 contact sheet,
      4) STAGE 1: run focused multi-pass detection/localization,
      5) run a targeted missing-coronary refinement + verification pass,
      6) convert each 2D point into an approximate 3D surface anchor,
      7) STAGE 2: generate detailed Vietnamese descriptions in small batches,
      8) merge the semantic content with the detected anchors,
      9) deactivate stale AI rows and upsert the newest generation.

    Stage 1 uses multiple focused passes for chambers, systemic vessels,
    pulmonary arteries, pulmonary veins, metadata evidence, coronary arteries,
    coronary veins, thin surface vessels, and open discovery. Thin-vessel
    passes receive eight separate high-resolution renders.

    Stage 2 then writes the detailed Vietnamese educational content in batches,
    so a 6000-token response budget is not shared by all 15-25 parts at once.
    """
    pipeline_started = time.monotonic()

    try:
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            glb_path = temp_path / "model.glb"

            # ============================================================
            # Download
            # ============================================================
            t0 = time.monotonic()
            download_model(request.model_url, glb_path)
            download_seconds = round(time.monotonic() - t0, 2)

            print(
                f"[identify-parts] download={download_seconds}s",
                flush=True,
            )

            # ============================================================
            # Analyze GLB metadata
            # ============================================================
            t0 = time.monotonic()
            analysis = analyze_glb(str(glb_path))
            analyze_seconds = round(time.monotonic() - t0, 2)

            print(
                f"[identify-parts] analyze={analyze_seconds}s",
                flush=True,
            )

            analysis_json = json.dumps(
                analysis,
                ensure_ascii=False,
                separators=(",", ":"),
                default=str,
            )

            if len(analysis_json) > 7000:
                analysis_json = analysis_json[:7000] + "...<truncated>"

            # ============================================================
            # Render 16 views
            # ============================================================
            t0 = time.monotonic()
            rendered_images = []

            for index, view in enumerate(IDENTIFY_VIEWS, start=1):
                render_path = temp_path / f"{view}.png"

                print(
                    f"[identify-parts] render "
                    f"{index}/{len(IDENTIFY_VIEWS)}: {view}",
                    flush=True,
                )

                render_points_cpu(
                    file_path=str(glb_path),
                    output_path=str(render_path),
                    view=view,
                    image_size=IDENTIFY_IMAGE_SIZE,
                    max_points=IDENTIFY_MAX_POINTS,
                )

                if not render_path.exists():
                    raise RuntimeError(
                        f"Failed to render {view} view."
                    )

                with Image.open(render_path) as image:
                    rendered_images.append(
                        image.convert("RGB").copy()
                    )

            render_seconds = round(
                time.monotonic() - t0,
                2,
            )

            # ============================================================
            # 4 x 4 contact sheet
            # ============================================================
            sheet_columns = 4
            sheet_rows = 4
            sheet_width = IDENTIFY_IMAGE_SIZE * sheet_columns
            sheet_height = IDENTIFY_IMAGE_SIZE * sheet_rows

            contact_sheet = Image.new(
                "RGB",
                (sheet_width, sheet_height),
                (245, 247, 250),
            )

            positions = []

            for index in range(len(rendered_images)):
                row = index // sheet_columns
                column = index % sheet_columns

                positions.append(
                    (
                        column * IDENTIFY_IMAGE_SIZE,
                        row * IDENTIFY_IMAGE_SIZE,
                    )
                )

            for image, position in zip(
                rendered_images,
                positions,
            ):
                contact_sheet.paste(
                    image,
                    position,
                )

            # ============================================================
            # High-detail coronary contact sheet
            # ============================================================
            # The general 16-view sheet is optimized for broad anatomical
            # coverage. Thin coronary vessels can disappear at 320 px and
            # in a 4x4 layout, so render a second, higher-resolution sheet
            # containing the eight external ring views only.
            coronary_images = []

            for index, view in enumerate(CORONARY_VIEWS, start=1):
                coronary_path = temp_path / f"coronary_{view}.png"

                print(
                    f"[identify-parts] coronary render "
                    f"{index}/{len(CORONARY_VIEWS)}: {view}",
                    flush=True,
                )

                render_points_cpu(
                    file_path=str(glb_path),
                    output_path=str(coronary_path),
                    view=view,
                    image_size=CORONARY_IMAGE_SIZE,
                    max_points=CORONARY_MAX_POINTS,
                )

                if not coronary_path.exists():
                    raise RuntimeError(
                        f"Failed to render coronary view: {view}"
                    )

                with Image.open(coronary_path) as image:
                    coronary_images.append(
                        image.convert("RGB").copy()
                    )

            coronary_columns = 4
            coronary_rows = 2

            coronary_contact_sheet = Image.new(
                "RGB",
                (
                    CORONARY_IMAGE_SIZE * coronary_columns,
                    CORONARY_IMAGE_SIZE * coronary_rows,
                ),
                (245, 247, 250),
            )

            for index, image in enumerate(coronary_images):
                row = index // coronary_columns
                column = index % coronary_columns

                coronary_contact_sheet.paste(
                    image,
                    (
                        column * CORONARY_IMAGE_SIZE,
                        row * CORONARY_IMAGE_SIZE,
                    ),
                )

            # ============================================================
            # STAGE 1 — MULTI-PASS DETECTION / LOCALIZATION
            # ============================================================
            #
            # A single large detection prompt tended to saturate around
            # ~10 parts even with 16 viewpoints. We now run several focused
            # passes over the SAME contact sheet. Each pass has a smaller
            # anatomical search space, then all results are merged by
            # canonical English part_key and the highest-confidence
            # localization wins.
            # ============================================================

            common_detection_context = f"""
The attached image is a 4-column x 4-row contact sheet containing
SIXTEEN views in exactly this order:

Row 1:
1. front
2. front_right
3. right
4. back_right

Row 2:
1. back
2. back_left
3. left
4. front_left

Row 3:
1. top
2. bottom
3. top_front
4. top_right

Row 4:
1. top_back
2. top_left
3. bottom_front
4. bottom_back

Compact GLB metadata:
{analysis_json}

This is a DETECTION AND LOCALIZATION pass only.

Do NOT write descriptions.
Do NOT write structure explanations.
Do NOT write function explanations.

Inspect ALL 16 views before deciding that a candidate is absent.

For every returned structure provide ONLY:
- part_name_en: canonical English anatomical/scientific name
- part_name_vi: standard Vietnamese anatomical/scientific name
- best_view: exactly one of:
  front | front_right | right | back_right |
  back | back_left | left | front_left |
  top | bottom | top_front | top_right |
  top_back | top_left | bottom_front | bottom_back
- normalized_x: 0 to 1 inside the selected individual view
- normalized_y: 0 to 1 inside the selected individual view
- confidence: 0 to 1 confidence in BOTH identity and location

Coordinate convention:
x = 0 left
x = 1 right
y = 0 top
y = 1 bottom

Quality rules:
- Return a candidate only if its identity and approximate location are
  reasonably supported by the rendered model.
- Never invent a structure merely to increase the count.
- Use oblique views to separate overlapping vessels and chambers.
- Do not return synonyms or duplicate names for the same structure.
- Prefer the view where the structure is most visually separable.
- For small vessels, inspect diagonal and superior/inferior views carefully.
""".strip()

            detection_passes = [
                {
                    "name": "chambers_support",
                    "instructions": """
This pass focuses ONLY on cardiac chambers and supporting/external structures.

Actively inspect for:
- Right Atrium
- Left Atrium
- Right Ventricle
- Left Ventricle
- Right Auricle
- Left Auricle
- Apex of the Heart
- Interventricular Septum

Return every candidate from this group that is actually visible or strongly
supported. Do not spend output on vessels in this pass.
""".strip(),
                },
                {
                    "name": "aorta_vena_cava",
                    "instructions": """
This pass focuses ONLY on the systemic great vessels.

Actively inspect for:
- Aorta
- Ascending Aorta
- Aortic Arch
- Superior Vena Cava
- Inferior Vena Cava

Inspect front, front_right, top, top_front, top_right, back, and back_right.
Try to distinguish the ascending segment from the aortic arch only when the
geometry visibly supports that subdivision.

Return every supported structure from this group.
""".strip(),
                },
                {
                    "name": "pulmonary_arteries",
                    "instructions": """
This pass focuses ONLY on pulmonary arterial structures.

Actively inspect for:
- Pulmonary Trunk
- Right Pulmonary Artery
- Left Pulmonary Artery

Trace the pulmonary trunk from the heart base and look for a visible
bifurcation into right and left pulmonary arteries.

Use top_front, top_right, top_left, front_right, front_left, back_right,
and back_left to resolve overlap.

Return each branch separately only when its course is visibly distinguishable.
""".strip(),
                },
                {
                    "name": "pulmonary_veins",
                    "instructions": """
This pass focuses ONLY on pulmonary venous structures.

Actively inspect for:
- Right Superior Pulmonary Vein
- Right Inferior Pulmonary Vein
- Left Superior Pulmonary Vein
- Left Inferior Pulmonary Vein
- Right Pulmonary Veins, if individual branches cannot be separated
- Left Pulmonary Veins, if individual branches cannot be separated

Inspect back, back_right, back_left, left, right, top_back, top_left,
and top_right because pulmonary veins are commonly best seen posteriorly.

Do not return both a grouped pulmonary-veins label and its individual
superior/inferior branches unless the model clearly supports both levels.
""".strip(),
                },
                {
                    "name": "coronary_arteries",
                    "use_coronary_images": True,
                    "min_confidence": CORONARY_MIN_CONFIDENCE,
                    "instructions": """
This pass focuses ONLY on coronary ARTERIES visible on the external heart
surface.

Actively inspect for:
- Right Coronary Artery
- Left Coronary Artery
- Anterior Interventricular Artery
  (Left Anterior Descending / LAD when appropriate)
- Circumflex Artery

Use vessel course, branching pattern, surface position, and color/geometry
separation when the model provides them.

Thin coronary arteries can be subtle. A candidate may be returned at moderate
confidence when its visible course is anatomically consistent and localized,
but do not invent a vessel that is not represented on the model.
""".strip(),
                },
                {
                    "name": "coronary_veins",
                    "use_coronary_images": True,
                    "min_confidence": CORONARY_MIN_CONFIDENCE,
                    "instructions": """
This pass focuses ONLY on CARDIAC VEINS visible on the external heart surface.

Actively inspect for:
- Great Cardiac Vein
- Small Cardiac Vein
- Middle Cardiac Vein
- Coronary Sinus, only if externally visible/supported

Trace thin venous structures across the ventricular surface and posterior
heart. Use their course and relationship to neighboring coronary arteries.

Return only structures whose path is actually visible or strongly supported
by the model.
""".strip(),
                },
                {
                    "name": "metadata_structure_names",
                    "instructions": """
This pass focuses on STRUCTURE NAMES OR MESH/NODE EVIDENCE present in the GLB
metadata.

Use the compact GLB metadata to identify any anatomical structures whose
mesh/node/geometry naming strongly indicates a real modeled structure.

Important:
- Do not invent anatomy from general knowledge.
- Only return a structure if the metadata itself provides meaningful evidence.
- If metadata names are generic, return nothing from this pass.
- Choose best_view and approximate coordinates only when the structure can
  also be localized in the rendered images.

This pass is intended to recover valid modeled structures that may be visually
small or partially occluded.
""".strip(),
                },
                {
                    "name": "surface_vessel_discovery",
                    "use_coronary_images": True,
                    "min_confidence": CORONARY_MIN_CONFIDENCE,
                    "instructions": """
This pass is a focused discovery pass for THIN EXTERNAL SURFACE VESSELS.

Inspect the high-resolution external views for any clearly represented vessel
that was not already covered by the standard coronary checklist.

Possible examples include:
- Posterior Interventricular Artery
- Marginal branches
- Diagonal branches
- Posterior cardiac veins
- Coronary Sinus, if externally visible
- Other visibly distinct coronary branches

Do not infer invisible branches from textbook anatomy alone. Return only
surface vessels with an actual visible course on this model.
""".strip(),
                },
                {
                    "name": "discovery",
                    "instructions": """
This is an open discovery pass for meaningful anatomical structures that are
clearly visible in the model but may not have been covered by the three
focused lists.

Look for additional distinct structures, vessel subdivisions, appendages,
grooves, or externally visible anatomical landmarks.

Do NOT repeat obvious structures already likely found in the focused passes.
Do NOT invent hidden internal anatomy that is not supported by the rendered
surface model.

Aim to recover genuinely visible structures that a standard checklist could
miss.
""".strip(),
                },
            ]

            detection_started = time.monotonic()

            detected_by_key = {}
            detection_pass_counts = {}
            detection_models = []
            detection_attempts = []

            for pass_index, detection_pass in enumerate(
                detection_passes,
                start=1,
            ):
                pass_name = detection_pass["name"]

                if detection_pass.get("use_coronary_images", False):
                    image_layout_context = """
For THIS pass, EIGHT high-resolution images are attached separately and in
this exact order:

1. front
2. front_right
3. right
4. back_right
5. back
6. back_left
7. left
8. front_left

Each image is a high-resolution external render. Inspect each image independently
instead of treating them as tiles in a single sheet. This pass is optimized
for thin external vessels.
""".strip()
                else:
                    image_layout_context = common_detection_context

                detection_prompt = f"""
Analyze this educational 3D anatomical/scientific model.

{image_layout_context}

FOCUSED PASS:
{detection_pass["instructions"]}

Return ONLY valid JSON with this exact schema:

{{
  "parts": [
    {{
      "part_name_en": "Aortic Arch",
      "part_name_vi": "Cung động mạch chủ",
      "best_view": "top_front",
      "normalized_x": 0.55,
      "normalized_y": 0.30,
      "confidence": 0.90
    }}
  ]
}}
""".strip()

                if detection_pass.get("use_coronary_images", False):
                    detection_contents = [
                        detection_prompt,
                        *coronary_images,
                    ]
                else:
                    detection_contents = [
                        detection_prompt,
                        contact_sheet,
                    ]

                (
                    detection_response,
                    detection_model,
                    detection_attempt,
                    detection_call_seconds,
                ) = call_gemini_with_retry(
                    contents=detection_contents,
                    max_output_tokens=DETECTION_MAX_OUTPUT_TOKENS,
                    stage=f"detection-{pass_name}",
                )

                detection_models.append(detection_model)
                detection_attempts.append(detection_attempt)

                raw_detection = (
                    detection_response.text
                    or ""
                ).strip()

                if not raw_detection:
                    raise HTTPException(
                        status_code=502,
                        detail=(
                            f"Gemini detection pass "
                            f"{pass_name!r} returned empty text."
                        ),
                    )

                cleaned_detection = clean_json_response(
                    raw_detection
                )

                try:
                    detection_json = json.loads(
                        cleaned_detection
                    )

                    print(
                        "[identify-parts] "
                        f"detection_pass={pass_name} "
                        f"json_shape={type(detection_json).__name__}",
                        flush=True,
                    )
                except json.JSONDecodeError:
                    raise HTTPException(
                        status_code=502,
                        detail={
                            "message": (
                                "Gemini detection pass returned "
                                "non-JSON output."
                            ),
                            "pass": pass_name,
                            "model_used": detection_model,
                            "raw_response": cleaned_detection[:3000],
                        },
                    )

                detected_items = extract_parts_list(
                    detection_json,
                    stage_name=(
                        f"Gemini detection pass {pass_name!r}"
                    ),
                )

                valid_in_pass = 0

                for item in detected_items[:25]:
                    # Ignore malformed/nested items instead of crashing.
                    if not isinstance(item, dict):
                        continue

                    part_name_en = str(
                        item.get(
                            "part_name_en",
                            item.get("part_name", ""),
                        )
                    ).strip()

                    part_name_vi = str(
                        item.get(
                            "part_name_vi",
                            item.get("part_name", ""),
                        )
                    ).strip()

                    best_view = str(
                        item.get("best_view", "")
                    ).strip().lower()

                    if (
                        not part_name_en
                        or not part_name_vi
                        or best_view not in IDENTIFY_VIEWS
                    ):
                        continue

                    try:
                        normalized_x = float(
                            item.get("normalized_x")
                        )
                        normalized_y = float(
                            item.get("normalized_y")
                        )
                        confidence = float(
                            item.get("confidence", 0)
                        )
                    except (TypeError, ValueError):
                        continue

                    normalized_x = max(
                        0.0,
                        min(1.0, normalized_x),
                    )
                    normalized_y = max(
                        0.0,
                        min(1.0, normalized_y),
                    )
                    confidence = max(
                        0.0,
                        min(1.0, confidence),
                    )

                    pass_min_confidence = float(
                        detection_pass.get(
                            "min_confidence",
                            IDENTIFY_MIN_CONFIDENCE,
                        )
                    )

                    if confidence < pass_min_confidence:
                        continue

                    try:
                        part_key = make_part_key(
                            part_name_en
                        )
                    except ValueError:
                        continue

                    candidate = {
                        "part_key": part_key,
                        "part_name_en": part_name_en,
                        "part_name_vi": part_name_vi,
                        "part_name": part_name_vi,
                        "best_view": best_view,
                        "normalized_x": normalized_x,
                        "normalized_y": normalized_y,
                        "confidence": confidence,
                        "detection_pass": pass_name,
                    }

                    existing = detected_by_key.get(
                        part_key
                    )

                    # If two passes find the same canonical structure,
                    # keep the higher-confidence localization.
                    if (
                        existing is None
                        or confidence > existing["confidence"]
                    ):
                        detected_by_key[
                            part_key
                        ] = candidate

                    valid_in_pass += 1

                detection_pass_counts[
                    pass_name
                ] = valid_in_pass

                print(
                    "[identify-parts] "
                    f"detection_pass={pass_name} "
                    f"valid={valid_in_pass} "
                    f"merged={len(detected_by_key)} "
                    f"seconds={detection_call_seconds}",
                    flush=True,
                )

            # ============================================================
            # STAGE 1B — TARGETED MISSING-CORONARY REFINEMENT
            # ============================================================
            # At this point the broad detector is usually strong on chambers,
            # great vessels and pulmonary vessels, but thin coronary vessels may
            # still be absent. Only search for canonical targets that are still
            # missing, then verify those candidates in a second call before merge.
            # This prevents repeated regeneration of already-good structures and
            # reduces false positives compared with simply lowering the global
            # confidence threshold.
            # ============================================================

            refinement_started = time.monotonic()

            missing_refinement_targets = []

            for target in CORONARY_REFINEMENT_TARGETS:
                target_key = make_part_key(
                    target["part_name_en"]
                )

                if target_key not in detected_by_key:
                    missing_refinement_targets.append({
                        **target,
                        "part_key": target_key,
                    })

            refinement_candidate_count = 0
            refinement_verified_count = 0
            refinement_candidates = []

            if missing_refinement_targets:
                target_json = json.dumps(
                    missing_refinement_targets,
                    ensure_ascii=False,
                    separators=(",", ":"),
                )

                refinement_prompt = f"""
You are performing a TARGETED REFINE-MISSING-STRUCTURES pass on a 3D HUMAN
HEART model.

Eight high-resolution external images are attached separately and in this
exact order:

1. front
2. front_right
3. right
4. back_right
5. back
6. back_left
7. left
8. front_left

The normal detector has already found many structures. Search ONLY for the
following structures that are still missing:

{target_json}

Important:
- Do NOT return structures outside the supplied target list.
- Inspect all eight images independently.
- Thin coronary vessels may be subtle; use visible vessel course, branching,
  color/material separation, and anatomical surface position.
- A structure may be proposed at moderate confidence when there is real visual
  evidence, but do not invent a vessel from textbook knowledge alone.
- Return at most one localization per target.
- Use the canonical supplied English/Vietnamese names exactly.

For every proposed candidate return:
- part_key: copy the supplied part_key exactly
- part_name_en: copy the supplied English name exactly
- part_name_vi: copy the supplied Vietnamese name exactly
- best_view: one of
  front | front_right | right | back_right |
  back | back_left | left | front_left
- normalized_x: 0..1 inside that individual view
- normalized_y: 0..1 inside that individual view
- confidence: 0..1

Return ONLY valid JSON:

{{
  "parts": [
    {{
      "part_key": "right_coronary_artery",
      "part_name_en": "Right Coronary Artery",
      "part_name_vi": "Động mạch vành phải",
      "best_view": "front_right",
      "normalized_x": 0.55,
      "normalized_y": 0.58,
      "confidence": 0.62
    }}
  ]
}}
""".strip()

                (
                    refinement_response,
                    refinement_model,
                    refinement_attempt,
                    refinement_call_seconds,
                ) = call_gemini_with_retry(
                    contents=[
                        refinement_prompt,
                        *coronary_images,
                    ],
                    max_output_tokens=CORONARY_REFINEMENT_MAX_OUTPUT_TOKENS,
                    stage="coronary-refinement-candidates",
                )

                detection_models.append(
                    refinement_model
                )
                detection_attempts.append(
                    refinement_attempt
                )

                raw_refinement = (
                    refinement_response.text
                    or ""
                ).strip()

                if raw_refinement:
                    cleaned_refinement = clean_json_response(
                        raw_refinement
                    )

                    try:
                        refinement_json = json.loads(
                            cleaned_refinement
                        )

                        refinement_items = extract_parts_list(
                            refinement_json,
                            stage_name=(
                                "Gemini coronary refinement candidates"
                            ),
                        )
                    except json.JSONDecodeError:
                        refinement_items = []

                    allowed_target_keys = {
                        item["part_key"]
                        for item in missing_refinement_targets
                    }

                    for item in refinement_items:
                        if not isinstance(item, dict):
                            continue

                        part_key = str(
                            item.get("part_key", "")
                        ).strip()

                        if part_key not in allowed_target_keys:
                            continue

                        best_view = str(
                            item.get("best_view", "")
                        ).strip().lower()

                        if best_view not in CORONARY_VIEWS:
                            continue

                        try:
                            normalized_x = float(
                                item.get("normalized_x")
                            )
                            normalized_y = float(
                                item.get("normalized_y")
                            )
                            confidence = float(
                                item.get("confidence", 0)
                            )
                        except (TypeError, ValueError):
                            continue

                        normalized_x = max(
                            0.0,
                            min(1.0, normalized_x),
                        )
                        normalized_y = max(
                            0.0,
                            min(1.0, normalized_y),
                        )
                        confidence = max(
                            0.0,
                            min(1.0, confidence),
                        )

                        if confidence < CORONARY_REFINEMENT_MIN_CONFIDENCE:
                            continue

                        target = next(
                            (
                                target
                                for target in missing_refinement_targets
                                if target["part_key"] == part_key
                            ),
                            None,
                        )

                        if target is None:
                            continue

                        refinement_candidates.append({
                            "part_key": part_key,
                            "part_name_en": target["part_name_en"],
                            "part_name_vi": target["part_name_vi"],
                            "part_name": target["part_name_vi"],
                            "best_view": best_view,
                            "normalized_x": normalized_x,
                            "normalized_y": normalized_y,
                            "confidence": confidence,
                            "detection_pass": "coronary_refinement_candidate",
                        })

                refinement_candidate_count = len(
                    refinement_candidates
                )

            # ------------------------------------------------------------
            # Verification pass
            # ------------------------------------------------------------
            if refinement_candidates:
                verification_payload = [
                    {
                        "part_key": item["part_key"],
                        "part_name_en": item["part_name_en"],
                        "part_name_vi": item["part_name_vi"],
                        "best_view": item["best_view"],
                        "normalized_x": item["normalized_x"],
                        "normalized_y": item["normalized_y"],
                        "candidate_confidence": item["confidence"],
                    }
                    for item in refinement_candidates
                ]

                verification_json_text = json.dumps(
                    verification_payload,
                    ensure_ascii=False,
                    separators=(",", ":"),
                )

                verification_prompt = f"""
Verify the following candidate coronary/cardiac surface structures on the same
3D HUMAN HEART model.

Eight high-resolution images are attached separately in order:
front, front_right, right, back_right, back, back_left, left, front_left.

Candidate list:
{verification_json_text}

For EACH candidate:
- Decide whether the proposed anatomical identity is genuinely supported.
- Check whether the proposed view and point lie on the visible structure.
- Reject candidates that merely match textbook expectations but are not
  actually visible.
- If supported, you may slightly correct best_view / normalized_x /
  normalized_y.
- Return a final confidence that reflects BOTH identity and localization.

Return ONLY candidates you VERIFY.

Return ONLY valid JSON:

{{
  "parts": [
    {{
      "part_key": "right_coronary_artery",
      "best_view": "front_right",
      "normalized_x": 0.55,
      "normalized_y": 0.58,
      "confidence": 0.68
    }}
  ]
}}
""".strip()

                (
                    verification_response,
                    verification_model,
                    verification_attempt,
                    verification_call_seconds,
                ) = call_gemini_with_retry(
                    contents=[
                        verification_prompt,
                        *coronary_images,
                    ],
                    max_output_tokens=CORONARY_REFINEMENT_MAX_OUTPUT_TOKENS,
                    stage="coronary-refinement-verification",
                )

                detection_models.append(
                    verification_model
                )
                detection_attempts.append(
                    verification_attempt
                )

                raw_verification = (
                    verification_response.text
                    or ""
                ).strip()

                verified_items = []

                if raw_verification:
                    cleaned_verification = clean_json_response(
                        raw_verification
                    )

                    try:
                        verification_json = json.loads(
                            cleaned_verification
                        )

                        verified_items = extract_parts_list(
                            verification_json,
                            stage_name=(
                                "Gemini coronary refinement verification"
                            ),
                        )
                    except json.JSONDecodeError:
                        verified_items = []

                candidate_by_key = {
                    item["part_key"]: item
                    for item in refinement_candidates
                }

                for item in verified_items:
                    if not isinstance(item, dict):
                        continue

                    part_key = str(
                        item.get("part_key", "")
                    ).strip()

                    candidate = candidate_by_key.get(
                        part_key
                    )

                    if candidate is None:
                        continue

                    best_view = str(
                        item.get(
                            "best_view",
                            candidate["best_view"],
                        )
                    ).strip().lower()

                    if best_view not in CORONARY_VIEWS:
                        best_view = candidate["best_view"]

                    try:
                        normalized_x = float(
                            item.get(
                                "normalized_x",
                                candidate["normalized_x"],
                            )
                        )
                        normalized_y = float(
                            item.get(
                                "normalized_y",
                                candidate["normalized_y"],
                            )
                        )
                        confidence = float(
                            item.get("confidence", 0)
                        )
                    except (TypeError, ValueError):
                        continue

                    normalized_x = max(
                        0.0,
                        min(1.0, normalized_x),
                    )
                    normalized_y = max(
                        0.0,
                        min(1.0, normalized_y),
                    )
                    confidence = max(
                        0.0,
                        min(1.0, confidence),
                    )

                    if confidence < CORONARY_VERIFIED_MIN_CONFIDENCE:
                        continue

                    verified_candidate = {
                        **candidate,
                        "best_view": best_view,
                        "normalized_x": normalized_x,
                        "normalized_y": normalized_y,
                        "confidence": confidence,
                        "detection_pass": "coronary_refinement_verified",
                    }

                    existing = detected_by_key.get(
                        part_key
                    )

                    if (
                        existing is None
                        or confidence > existing["confidence"]
                    ):
                        detected_by_key[
                            part_key
                        ] = verified_candidate

                        refinement_verified_count += 1

            detection_pass_counts[
                "coronary_refinement_candidates"
            ] = refinement_candidate_count

            detection_pass_counts[
                "coronary_refinement_verified"
            ] = refinement_verified_count

            refinement_seconds = round(
                time.monotonic()
                - refinement_started,
                2,
            )

            print(
                "[identify-parts] coronary_refinement "
                f"missing={len(missing_refinement_targets)} "
                f"candidates={refinement_candidate_count} "
                f"verified={refinement_verified_count} "
                f"seconds={refinement_seconds}",
                flush=True,
            )

            detection_seconds = round(
                time.monotonic()
                - detection_started,
                2,
            )

            # Sort by confidence, keep at most 25 merged structures.
            detected_parts = sorted(
                detected_by_key.values(),
                key=lambda item: item["confidence"],
                reverse=True,
            )[:25]

            if not detected_parts:
                raise HTTPException(
                    status_code=502,
                    detail=(
                        "Multi-pass detection produced no valid "
                        "anatomical parts."
                    ),
                )

            print(
                "[identify-parts] multi_pass_detection_valid="
                f"{len(detected_parts)} "
                f"counts={detection_pass_counts}",
                flush=True,
            )

            # ============================================================
            # 3D anchors
            # ============================================================
            t0 = time.monotonic()

            for part in detected_parts:
                anchor = normalized_point_to_3d_anchor(
                    file_path=str(glb_path),
                    view=part["best_view"],
                    normalized_x=part["normalized_x"],
                    normalized_y=part["normalized_y"],
                    image_size=IDENTIFY_IMAGE_SIZE,
                    max_points=30000,
                    candidate_count=64,
                    surface_band_pixels=6.0,
                )

                part["anchor_x"] = anchor["anchor_x"]
                part["anchor_y"] = anchor["anchor_y"]
                part["anchor_z"] = anchor["anchor_z"]

                part["anchor_debug"] = {
                    "pixel_x": anchor["pixel_x"],
                    "pixel_y": anchor["pixel_y"],
                    "matched_pixel_x": anchor["matched_pixel_x"],
                    "matched_pixel_y": anchor["matched_pixel_y"],
                    "pixel_distance": anchor["pixel_distance"],
                    "normalized_depth": anchor["normalized_depth"],
                }

            anchor_seconds = round(
                time.monotonic() - t0,
                2,
            )

            # ============================================================
            # STAGE 2 — DETAILED VIETNAMESE DESCRIPTIONS IN BATCHES
            # ============================================================
            detail_started = time.monotonic()
            detail_models = []
            description_by_key = {}

            for batch_start in range(
                0,
                len(detected_parts),
                DESCRIPTION_BATCH_SIZE,
            ):
                batch = detected_parts[
                    batch_start:
                    batch_start + DESCRIPTION_BATCH_SIZE
                ]

                batch_number = (
                    batch_start // DESCRIPTION_BATCH_SIZE
                ) + 1

                batch_payload = [
                    {
                        "part_key": item["part_key"],
                        "part_name_en": item["part_name_en"],
                        "part_name_vi": item["part_name_vi"],
                    }
                    for item in batch
                ]

                batch_json = json.dumps(
                    batch_payload,
                    ensure_ascii=False,
                    separators=(",", ":"),
                )

                detail_prompt = f"""
You are writing educational anatomy/science content for Vietnamese students.

The following structures have ALREADY been detected and localized from a
3D model. Do NOT add new structures and do NOT remove structures.

Compact GLB metadata:
{analysis_json}

Structures in this batch:
{batch_json}

For EACH structure in the batch, write detailed educational content entirely
in Vietnamese.

Return exactly:

- part_key:
  Copy the supplied part_key EXACTLY.

- description:
  Write 4 to 6 complete Vietnamese sentences.
  Explain what the structure is, its anatomical/scientific position,
  important relationships with neighboring structures, and why it matters.
  Keep the explanation concrete and specific.

- structure_description:
  Write 4 to 6 complete Vietnamese sentences.
  Explain the shape, orientation, wall/tissue/visible morphology when
  appropriate, anatomical boundaries, major subdivisions, and structural
  relationships that are scientifically relevant.

- function:
  Write 4 to 6 complete Vietnamese sentences.
  Explain the main physiological/mechanical function, how the structure
  performs that function, what enters/leaves or interacts with it when
  relevant, and how it contributes to the larger organ/system.

Requirements:
- All three content fields MUST be entirely in Vietnamese.
- Use accurate Vietnamese anatomical/scientific terminology.
- Do not insert markdown, bullet points, headings, or citations inside fields.
- Avoid repeating the same idea between the three fields.
- Do not invent pathology, measurements, disease, or patient-specific facts.
- Do not invent structures outside the supplied batch.
- Preserve educational clarity for secondary-school and university learners.
- Give substantial detail; do not reduce the content to one short sentence.

Return ONLY valid JSON:

{{
  "parts": [
    {{
      "part_key": "left_ventricle",
      "description": "...",
      "structure_description": "...",
      "function": "..."
    }}
  ]
}}
""".strip()

                (
                    detail_response,
                    detail_model,
                    _,
                    detail_call_seconds,
                ) = call_gemini_with_retry(
                    contents=[detail_prompt],
                    max_output_tokens=IDENTIFY_MAX_OUTPUT_TOKENS,
                    stage=f"detail-{batch_number}",
                )

                detail_models.append(
                    detail_model
                )

                raw_detail = (
                    detail_response.text
                    or ""
                ).strip()

                if not raw_detail:
                    raise HTTPException(
                        status_code=502,
                        detail=(
                            f"Gemini detail batch "
                            f"{batch_number} returned empty text."
                        ),
                    )

                cleaned_detail = clean_json_response(
                    raw_detail
                )

                try:
                    detail_json = json.loads(
                        cleaned_detail
                    )

                    print(
                        "[identify-parts] "
                        f"detail_batch={batch_number} "
                        f"json_shape={type(detail_json).__name__}",
                        flush=True,
                    )
                except json.JSONDecodeError:
                    raise HTTPException(
                        status_code=502,
                        detail={
                            "message": (
                                "Gemini detail stage returned "
                                "non-JSON output."
                            ),
                            "batch": batch_number,
                            "model_used": detail_model,
                            "raw_response": cleaned_detail[:3000],
                        },
                    )

                detail_items = extract_parts_list(
                    detail_json,
                    stage_name=(
                        f"Gemini detail batch {batch_number}"
                    ),
                )

                expected_batch_keys = {
                    item["part_key"]
                    for item in batch
                }

                for detail_item in detail_items:
                    if not isinstance(
                        detail_item,
                        dict,
                    ):
                        continue

                    key = str(
                        detail_item.get(
                            "part_key",
                            "",
                        )
                    ).strip()

                    if key not in expected_batch_keys:
                        continue

                    description_by_key[key] = {
                        "description": str(
                            detail_item.get(
                                "description",
                                "",
                            )
                        ).strip(),
                        "structure_description": str(
                            detail_item.get(
                                "structure_description",
                                detail_item.get(
                                    "structure",
                                    "",
                                ),
                            )
                        ).strip(),
                        "function": str(
                            detail_item.get(
                                "function",
                                "",
                            )
                        ).strip(),
                    }

                missing_keys = [
                    item["part_key"]
                    for item in batch
                    if item["part_key"] not in description_by_key
                ]

                if missing_keys:
                    raise HTTPException(
                        status_code=502,
                        detail={
                            "message": (
                                "Gemini detail stage omitted "
                                "one or more detected parts."
                            ),
                            "batch": batch_number,
                            "missing_part_keys": missing_keys,
                        },
                    )

            detail_seconds = round(
                time.monotonic() - detail_started,
                2,
            )

            # ============================================================
            # Merge Stage 1 + Stage 2
            # ============================================================
            validated_parts = []

            for part in detected_parts:
                content = description_by_key[
                    part["part_key"]
                ]

                validated_parts.append({
                    **part,
                    "description": content["description"],
                    "structure_description": content[
                        "structure_description"
                    ],
                    "function": content["function"],
                })

            # ============================================================
            # Save newest generation
            # ============================================================
            t0 = time.monotonic()

            deactivated_count = deactivate_existing_ai_parts(
                asset_id=request.asset_id,
            )

            all_models = [
                *detection_models,
                *detail_models,
            ]

            unique_models = []

            for model_name in all_models:
                if model_name not in unique_models:
                    unique_models.append(model_name)

            model_used_summary = ",".join(
                unique_models
            )

            saved_rows = upsert_model_parts(
                asset_id=request.asset_id,
                parts=validated_parts,
                model_used=model_used_summary,
            )

            save_seconds = round(
                time.monotonic() - t0,
                2,
            )

            total_seconds = round(
                time.monotonic()
                - pipeline_started,
                2,
            )

            print(
                "[identify-parts] "
                f"detected={len(detected_parts)} "
                f"saved={len(saved_rows)} "
                f"deactivated={deactivated_count} "
                f"detail_batches="
                f"{len(range(0, len(detected_parts), DESCRIPTION_BATCH_SIZE))} "
                f"total={total_seconds}s",
                flush=True,
            )

            return {
                "success": True,
                "pipeline_mode": "multi_pass_detection_then_batched_description",
                "model_used": model_used_summary,
                "detection_models": list(dict.fromkeys(detection_models)),
                "detection_attempts": detection_attempts,
                "detection_pass_counts": detection_pass_counts,
                "asset_id": str(request.asset_id),
                "part_count": len(validated_parts),
                "detected_count": len(detected_parts),
                "saved_count": len(saved_rows),
                "deactivated_count": deactivated_count,
                "description_batch_size": DESCRIPTION_BATCH_SIZE,
                "description_batch_count": len(
                    range(
                        0,
                        len(detected_parts),
                        DESCRIPTION_BATCH_SIZE,
                    )
                ),
                "database_saved": True,
                "timing": {
                    "download_seconds": download_seconds,
                    "analyze_seconds": analyze_seconds,
                    "render_seconds": render_seconds,
                    "detection_seconds": detection_seconds,
                    "refinement_seconds": refinement_seconds,
                    "anchor_seconds": anchor_seconds,
                    "detail_seconds": detail_seconds,
                    "save_seconds": save_seconds,
                    "total_seconds": total_seconds,
                },
                "parts": validated_parts,
            }

    except requests.RequestException as exc:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to download GLB model: {exc}",
        )

    except HTTPException:
        raise

    except Exception as exc:
        raise HTTPException(
            status_code=(
                503
                if is_retryable_gemini_error(exc)
                else 500
            ),
            detail=str(exc),
        )

