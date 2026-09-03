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


APP_VERSION = "1.8.0"

GEMINI_MODELS = [
    "gemini-3.5-flash-lite",
    "gemini-3.6-flash",
]

# Keep every inference bounded.
# Primary model is the low-latency Gemini 3.5 Flash-Lite.
# Fallback gets a slightly longer timeout.
GEMINI_TIMEOUT_MS = 30000
GEMINI_FALLBACK_TIMEOUT_MS = 45000

# Keep the multimodal payload intentionally small.
IDENTIFY_VIEWS = ["front", "back", "left", "right"]
IDENTIFY_IMAGE_SIZE = 256
IDENTIFY_MAX_POINTS = 12000
IDENTIFY_MAX_OUTPUT_TOKENS = 4500



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


def call_gemini_with_retry(contents):
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
                f"[Gemini] model={model_name} timeout={timeout_ms}ms",
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
                    max_output_tokens=IDENTIFY_MAX_OUTPUT_TOKENS,
                    response_mime_type="application/json",
                ),
            )

            elapsed = round(time.monotonic() - started, 2)

            if response is None:
                raise RuntimeError(
                    "Gemini returned an empty response object."
                )

            print(
                f"[Gemini] success model={model_name} elapsed={elapsed}s",
                flush=True,
            )
            return response, model_name, 1, elapsed

        except Exception as exc:
            last_error = exc
            elapsed = round(time.monotonic() - started, 2)
            print(
                f"[Gemini] failed model={model_name} elapsed={elapsed}s: {exc}",
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
        part_name = str(part.get("part_name", "")).strip()
        if not part_name:
            continue

        part_key = make_part_key(part_name)

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
        }

        payload.append({
            "asset_id": str(asset_id),
            "part_key": part_key,
            "part_name": part_name,
            "description": str(part.get("description", "")).strip(),
            "structure_description": str(part.get("structure", "")).strip(),
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
    allowed_views = {
        "front",
        "back",
        "left",
        "right",
        "top",
        "bottom",
    }

    selected_view = request.view.strip().lower()

    if selected_view not in allowed_views:
        raise HTTPException(
            status_code=400,
            detail=(
                "Invalid view. Allowed values: "
                "front, back, left, right, top, bottom."
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
    # Four orthogonal views are enough for the first semantic pass and cut
    # render/upload latency by ~1/3 versus six views.
    views = [
        "front",
        "back",
        "left",
        "right",
    ]

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


@app.post("/identify-parts")
def identify_parts(request: IdentifyPartsRequest):
    """
    Fast semantic pass:
      1) download and analyze the GLB,
      2) render four low-cost orthogonal views,
      3) combine them into one 2x2 contact sheet,
      4) ask Gemini for major parts + normalized 2D anchors,
      5) convert each 2D point into an approximate 3D surface anchor,
      6) upsert the semantic parts and anchors into Supabase model_parts.

    The contact sheet cuts multimodal request overhead compared with
    sending six separate images. 3D anchors are stored in GLB scene/root coordinates.
    """
    pipeline_started = time.monotonic()

    try:
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            glb_path = temp_path / "model.glb"

            t0 = time.monotonic()
            download_model(request.model_url, glb_path)
            download_seconds = round(time.monotonic() - t0, 2)
            print(
                f"[identify-parts] download={download_seconds}s",
                flush=True,
            )

            t0 = time.monotonic()
            analysis = analyze_glb(str(glb_path))
            analyze_seconds = round(time.monotonic() - t0, 2)
            print(
                f"[identify-parts] analyze={analyze_seconds}s",
                flush=True,
            )

            # Keep metadata useful but bounded. Large analyzer JSON can add
            # unnecessary prompt tokens and latency.
            analysis_json = json.dumps(
                analysis,
                ensure_ascii=False,
                separators=(",", ":"),
                default=str,
            )
            if len(analysis_json) > 7000:
                analysis_json = analysis_json[:7000] + "...<truncated>"

            t0 = time.monotonic()
            rendered_images = []

            for index, view in enumerate(IDENTIFY_VIEWS, start=1):
                render_path = temp_path / f"{view}.png"
                print(
                    f"[identify-parts] render {index}/4: {view}",
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

            render_seconds = round(time.monotonic() - t0, 2)

            # One 512x512 image instead of four independent image parts.
            sheet_size = IDENTIFY_IMAGE_SIZE * 2
            contact_sheet = Image.new(
                "RGB",
                (sheet_size, sheet_size),
                (245, 247, 250),
            )
            positions = [
                (0, 0),
                (IDENTIFY_IMAGE_SIZE, 0),
                (0, IDENTIFY_IMAGE_SIZE),
                (IDENTIFY_IMAGE_SIZE, IDENTIFY_IMAGE_SIZE),
            ]
            for image, position in zip(rendered_images, positions):
                contact_sheet.paste(image, position)

            prompt = f"""
Analyze this educational 3D anatomical/scientific model.

The ONE attached contact-sheet image contains four views:
- top-left = front
- top-right = back
- bottom-left = left
- bottom-right = right

Compact GLB metadata:
{analysis_json}

Return 5 to 10 major parts that are actually visible/supported.

This content will be shown to students in an educational detail panel.
Make the explanations meaningfully detailed, accurate, and readable.
Do NOT answer with one short sentence.

For each part return exactly:
- part_name
- description:
  3 to 5 complete sentences explaining what the part is, where it is located,
  what it connects to or is related to, and why it is important in the model.
- structure:
  2 to 4 complete sentences describing its visible/anatomical structure,
  shape, orientation, neighboring structures, and any important subdivisions
  that are supported by the model.
- function:
  3 to 5 complete sentences explaining its main function, how it performs that
  function, what enters/leaves or interacts with it when relevant, and its role
  in the larger organ/system.
- best_view: front | back | left | right
- normalized_x: 0..1 inside that individual view, NOT the whole contact sheet
- normalized_y: 0..1 inside that individual view, NOT the whole contact sheet
- confidence: 0..1

Writing requirements:
- Use educational language suitable for university/secondary-school learners.
- Prefer concrete anatomical/scientific relationships over generic statements.
- Avoid repeating the same sentence across description, structure, and function.
- Do not invent details that are not supported by the model or well-established
  anatomy/science.
- Keep each field detailed but concise enough for a mobile scroll panel.

Coordinate convention inside the chosen view:
x=0 left, x=1 right, y=0 top, y=1 bottom.
Point near the visual center of the named part.
Do not invent invisible parts.

Return ONLY valid JSON with this schema:
{{"parts":[{{"part_name":"Aorta","description":"3-5 sentences...","structure":"2-4 sentences...","function":"3-5 sentences...","best_view":"front","normalized_x":0.5,"normalized_y":0.25,"confidence":0.95}}]}}
""".strip()

            response, model_used, attempt, gemini_seconds = (
                call_gemini_with_retry(
                    contents=[prompt, contact_sheet]
                )
            )

            raw_text = (response.text or "").strip()
            if not raw_text:
                raise HTTPException(
                    status_code=502,
                    detail="Gemini returned an empty text response.",
                )

            cleaned_text = clean_json_response(raw_text)
            try:
                parsed = json.loads(cleaned_text)
            except json.JSONDecodeError:
                raise HTTPException(
                    status_code=502,
                    detail={
                        "message": "Gemini returned non-JSON output.",
                        "model_used": model_used,
                        "raw_response": cleaned_text[:3000],
                    },
                )

            parts = parsed.get("parts", [])
            if not isinstance(parts, list):
                raise HTTPException(
                    status_code=502,
                    detail="Gemini JSON field 'parts' is not a list.",
                )

            validated_parts = []
            for item in parts[:10]:
                if not isinstance(item, dict):
                    continue

                part_name = str(item.get("part_name", "")).strip()
                best_view = str(item.get("best_view", "")).strip().lower()

                if not part_name or best_view not in IDENTIFY_VIEWS:
                    continue

                try:
                    normalized_x = float(item.get("normalized_x"))
                    normalized_y = float(item.get("normalized_y"))
                    confidence = float(item.get("confidence", 0))
                except (TypeError, ValueError):
                    continue

                normalized_x = max(0.0, min(1.0, normalized_x))
                normalized_y = max(0.0, min(1.0, normalized_y))
                confidence = max(0.0, min(1.0, confidence))

                anchor = normalized_point_to_3d_anchor(
                    file_path=str(glb_path),
                    view=best_view,
                    normalized_x=normalized_x,
                    normalized_y=normalized_y,
                    image_size=IDENTIFY_IMAGE_SIZE,
                    max_points=30000,
                    candidate_count=64,
                    surface_band_pixels=6.0,
                )

                validated_parts.append({
                    "part_name": part_name,
                    "description": str(item.get("description", "")).strip(),
                    "structure": str(item.get("structure", "")).strip(),
                    "function": str(item.get("function", "")).strip(),
                    "best_view": best_view,
                    "normalized_x": normalized_x,
                    "normalized_y": normalized_y,
                    "confidence": confidence,
                    "anchor_x": anchor["anchor_x"],
                    "anchor_y": anchor["anchor_y"],
                    "anchor_z": anchor["anchor_z"],
                    "anchor_debug": {
                        "pixel_x": anchor["pixel_x"],
                        "pixel_y": anchor["pixel_y"],
                        "matched_pixel_x": anchor["matched_pixel_x"],
                        "matched_pixel_y": anchor["matched_pixel_y"],
                        "pixel_distance": anchor["pixel_distance"],
                        "normalized_depth": anchor["normalized_depth"],
                    },
                })

            anchor_seconds = round(
                time.monotonic() - (
                    pipeline_started
                    + download_seconds
                    + analyze_seconds
                    + render_seconds
                    + gemini_seconds
                ),
                2,
            )

            t0 = time.monotonic()

            saved_rows = upsert_model_parts(
                asset_id=request.asset_id,
                parts=validated_parts,
                model_used=model_used,
            )

            save_seconds = round(
                time.monotonic() - t0,
                2,
            )

            print(
                "[identify-parts] "
                f"supabase_saved={len(saved_rows)} "
                f"save={save_seconds}s",
                flush=True,
            )

            total_seconds = round(time.monotonic() - pipeline_started, 2)

            return {
                "success": True,
                "model_used": model_used,
                "attempt": attempt,
                "asset_id": str(request.asset_id),
                "part_count": len(validated_parts),
                "saved_count": len(saved_rows),
                "database_saved": True,
                "timing": {
                    "download_seconds": download_seconds,
                    "analyze_seconds": analyze_seconds,
                    "render_seconds": render_seconds,
                    "gemini_seconds": gemini_seconds,
                    "anchor_seconds": anchor_seconds,
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
            status_code=(503 if is_retryable_gemini_error(exc) else 500),
            detail=str(exc),
        )

