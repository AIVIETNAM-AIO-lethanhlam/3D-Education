from pathlib import Path

import numpy as np
import trimesh
from PIL import Image, ImageDraw


# =========================================================
# Common helpers
# =========================================================

def _vector3(value):
    value = np.asarray(
        value,
        dtype=float,
    )

    return [
        float(value[0]),
        float(value[1]),
        float(value[2]),
    ]


def load_glb_scene(file_path: str):
    """
    Load GLB as a trimesh.Scene.
    """

    path = Path(file_path)

    if not path.exists():
        raise FileNotFoundError(
            f"GLB file not found: {file_path}"
        )

    loaded = trimesh.load(
        str(path),
        force="scene",
        process=False,
    )

    if isinstance(
        loaded,
        trimesh.Scene
    ):
        return loaded

    return trimesh.Scene(
        loaded
    )


# =========================================================
# Model analysis
# =========================================================

def analyze_glb(file_path: str):
    """
    Analyze geometries contained in GLB.

    Returns:
    - mesh count
    - vertex count
    - triangle count
    - centroid
    - bounds
    - extents
    """

    path = Path(
        file_path
    )

    scene = load_glb_scene(
        file_path
    )

    result = {
        "file_name": path.name,
        "geometry_count":
            len(scene.geometry),
        "geometries": [],
    }

    for index, (
        name,
        geometry,
    ) in enumerate(
        scene.geometry.items()
    ):

        if not isinstance(
            geometry,
            trimesh.Trimesh
        ):
            continue

        if len(
            geometry.vertices
        ) == 0:
            continue

        bounds = (
            geometry.bounds
        )

        centroid = (
            geometry.centroid
        )

        extents = (
            geometry.extents
        )

        mesh_info = {
            "mesh_id": index,

            "mesh_name": name,

            "vertex_count": int(
                len(
                    geometry.vertices
                )
            ),

            "triangle_count": int(
                len(
                    geometry.faces
                )
            ),

            "centroid":
                _vector3(
                    centroid
                ),

            "bounds_min":
                _vector3(
                    bounds[0]
                ),

            "bounds_max":
                _vector3(
                    bounds[1]
                ),

            "extents":
                _vector3(
                    extents
                ),
        }

        result[
            "geometries"
        ].append(
            mesh_info
        )

    result["mesh_count"] = len(
        result["geometries"]
    )

    return result


# =========================================================
# View configuration
# =========================================================

VIEW_CONFIGS = {

    "front": {
        "right": np.array(
            [1.0, 0.0, 0.0],
            dtype=np.float32,
        ),
        "up": np.array(
            [0.0, 1.0, 0.0],
            dtype=np.float32,
        ),
        "camera": np.array(
            [0.0, 0.0, 1.0],
            dtype=np.float32,
        ),
    },

    "back": {
        "right": np.array(
            [-1.0, 0.0, 0.0],
            dtype=np.float32,
        ),
        "up": np.array(
            [0.0, 1.0, 0.0],
            dtype=np.float32,
        ),
        "camera": np.array(
            [0.0, 0.0, -1.0],
            dtype=np.float32,
        ),
    },

    "right": {
        "right": np.array(
            [0.0, 0.0, -1.0],
            dtype=np.float32,
        ),
        "up": np.array(
            [0.0, 1.0, 0.0],
            dtype=np.float32,
        ),
        "camera": np.array(
            [1.0, 0.0, 0.0],
            dtype=np.float32,
        ),
    },

    "left": {
        "right": np.array(
            [0.0, 0.0, 1.0],
            dtype=np.float32,
        ),
        "up": np.array(
            [0.0, 1.0, 0.0],
            dtype=np.float32,
        ),
        "camera": np.array(
            [-1.0, 0.0, 0.0],
            dtype=np.float32,
        ),
    },

    "top": {
        "right": np.array(
            [1.0, 0.0, 0.0],
            dtype=np.float32,
        ),
        "up": np.array(
            [0.0, 0.0, -1.0],
            dtype=np.float32,
        ),
        "camera": np.array(
            [0.0, 1.0, 0.0],
            dtype=np.float32,
        ),
    },

    "bottom": {
        "right": np.array(
            [1.0, 0.0, 0.0],
            dtype=np.float32,
        ),
        "up": np.array(
            [0.0, 0.0, 1.0],
            dtype=np.float32,
        ),
        "camera": np.array(
            [0.0, -1.0, 0.0],
            dtype=np.float32,
        ),
    },
}


# =========================================================
# Vertex extraction
# =========================================================

def _extract_vertices(
    scene: trimesh.Scene,
    max_points: int,
):
    """
    Extract sampled vertices in SCENE/ROOT coordinates.

    Unlike the old implementation, this function applies every GLB scene-node
    transform before sampling vertices. This is important for models whose
    meshes are translated, rotated, scaled, or instanced through the scene graph.

    Returns:
        np.ndarray with shape (N, 3), dtype float32.
    """

    vertex_groups = []

    # Each geometry can be referenced by one or more scene graph nodes.
    # scene.graph.get(node_name) returns:
    #   (4x4 transform from node -> scene/root, geometry_name)
    for node_name in scene.graph.nodes_geometry:
        transform, geometry_name = scene.graph.get(node_name)

        if geometry_name is None:
            continue

        geometry = scene.geometry.get(geometry_name)

        if not isinstance(geometry, trimesh.Trimesh):
            continue

        if len(geometry.vertices) == 0:
            continue

        vertices = np.asarray(
            geometry.vertices,
            dtype=np.float64,
        )

        # Apply the node's world/root transform. This also handles instanced
        # geometry correctly because each node is processed independently.
        transformed = trimesh.transform_points(
            vertices,
            transform,
        ).astype(np.float32)

        vertex_groups.append(transformed)

    # Fallback for unusual scenes that contain geometries but no geometry nodes.
    if not vertex_groups:
        for geometry in scene.geometry.values():
            if not isinstance(geometry, trimesh.Trimesh):
                continue

            if len(geometry.vertices) == 0:
                continue

            vertex_groups.append(
                np.asarray(
                    geometry.vertices,
                    dtype=np.float32,
                )
            )

    if not vertex_groups:
        raise ValueError(
            "No vertices found in GLB."
        )

    total_vertices = sum(
        len(vertices)
        for vertices in vertex_groups
    )

    if total_vertices <= max_points:
        return np.concatenate(
            vertex_groups,
            axis=0,
        )

    sampled_groups = []

    for vertices in vertex_groups:
        ratio = (
            len(vertices)
            / total_vertices
        )

        sample_count = max(
            1,
            int(
                max_points
                * ratio
            ),
        )

        sample_count = min(
            sample_count,
            len(vertices),
        )

        if sample_count == len(vertices):
            sampled = vertices
        else:
            indices = np.linspace(
                0,
                len(vertices) - 1,
                sample_count,
                dtype=np.int64,
            )

            sampled = vertices[
                indices
            ]

        sampled_groups.append(
            sampled
        )

    vertices = np.concatenate(
        sampled_groups,
        axis=0,
    )

    # Final safety limit.
    if len(vertices) > max_points:
        indices = np.linspace(
            0,
            len(vertices) - 1,
            max_points,
            dtype=np.int64,
        )

        vertices = vertices[
            indices
        ]

    return vertices


def _project_vertices_for_view(
    vertices: np.ndarray,
    view: str,
    image_size: int,
):
    """
    Project scene/root-space vertices to the exact same 2D coordinate system
    used by render_points_cpu().

    Returns a dictionary containing:
      - original_vertices: uncentered scene/root-space xyz
      - centered_vertices: xyz relative to the render center
      - projected_x / projected_y: pixel coordinates
      - depth / normalized_depth
      - center / bounds
    """

    selected_view = view.strip().lower()

    if selected_view not in VIEW_CONFIGS:
        raise ValueError(
            f"Unsupported view: {selected_view}"
        )

    vertices = np.asarray(
        vertices,
        dtype=np.float32,
    )

    if len(vertices) == 0:
        raise ValueError(
            "Model contains no usable vertices."
        )

    bounds_min = vertices.min(axis=0)
    bounds_max = vertices.max(axis=0)

    center = (
        bounds_min
        + bounds_max
    ) / 2.0

    centered_vertices = (
        vertices
        - center
    )

    config = VIEW_CONFIGS[
        selected_view
    ]

    right = config["right"]
    up = config["up"]
    camera = config["camera"]

    screen_x = (
        centered_vertices
        @ right
    )

    screen_y = (
        centered_vertices
        @ up
    )

    depth = (
        centered_vertices
        @ camera
    )

    min_x = float(
        screen_x.min()
    )
    max_x = float(
        screen_x.max()
    )
    min_y = float(
        screen_y.min()
    )
    max_y = float(
        screen_y.max()
    )

    projected_width = (
        max_x
        - min_x
    )
    projected_height = (
        max_y
        - min_y
    )

    model_size = max(
        projected_width,
        projected_height,
        1e-8,
    )

    # Must stay identical to render_points_cpu().
    usable_size = (
        image_size
        * 0.84
    )

    scale = (
        usable_size
        / model_size
    )

    center_x = (
        min_x
        + max_x
    ) / 2.0

    center_y = (
        min_y
        + max_y
    ) / 2.0

    projected_x = (
        (
            screen_x
            - center_x
        )
        * scale
        + image_size / 2.0
    )

    projected_y = (
        image_size / 2.0
        - (
            screen_y
            - center_y
        )
        * scale
    )

    depth_min = float(
        depth.min()
    )

    depth_max = float(
        depth.max()
    )

    depth_range = max(
        depth_max
        - depth_min,
        1e-8,
    )

    normalized_depth = (
        depth
        - depth_min
    ) / depth_range

    return {
        "view": selected_view,
        "original_vertices": vertices,
        "centered_vertices": centered_vertices,
        "projected_x": projected_x,
        "projected_y": projected_y,
        "depth": depth,
        "normalized_depth": normalized_depth,
        "center": center,
        "bounds_min": bounds_min,
        "bounds_max": bounds_max,
        "scale": float(scale),
    }


def normalized_point_to_3d_anchor(
    file_path: str,
    view: str,
    normalized_x: float,
    normalized_y: float,
    image_size: int = 256,
    max_points: int = 30000,
    candidate_count: int = 64,
    surface_band_pixels: float = 6.0,
):
    """
    Convert Gemini's normalized 2D point in one rendered view into an
    approximate 3D surface anchor in GLB scene/root coordinates.

    Method:
      1. Load the GLB and extract scene-transformed vertices.
      2. Project them using the same projection as render_points_cpu().
      3. Find vertices nearest to Gemini's target pixel.
      4. Within a small local pixel band, prefer the front-most vertex.

    normalized_x / normalized_y:
      - 0..1 relative to the individual rendered view.
      - x=0 left, x=1 right, y=0 top, y=1 bottom.

    Returns:
        {
          "anchor_x": ...,
          "anchor_y": ...,
          "anchor_z": ...,
          "pixel_x": ...,
          "pixel_y": ...,
          "matched_pixel_x": ...,
          "matched_pixel_y": ...,
          "pixel_distance": ...,
          "normalized_depth": ...
        }
    """

    try:
        nx = float(normalized_x)
        ny = float(normalized_y)
    except (TypeError, ValueError) as exc:
        raise ValueError(
            "normalized_x and normalized_y must be numbers."
        ) from exc

    nx = max(
        0.0,
        min(
            1.0,
            nx,
        ),
    )

    ny = max(
        0.0,
        min(
            1.0,
            ny,
        ),
    )

    scene = load_glb_scene(
        file_path
    )

    vertices = _extract_vertices(
        scene,
        max_points=max_points,
    )

    projection = _project_vertices_for_view(
        vertices=vertices,
        view=view,
        image_size=image_size,
    )

    projected_x = projection[
        "projected_x"
    ]

    projected_y = projection[
        "projected_y"
    ]

    normalized_depth = projection[
        "normalized_depth"
    ]

    target_x = (
        nx
        * (image_size - 1)
    )

    target_y = (
        ny
        * (image_size - 1)
    )

    distance_sq = (
        (
            projected_x
            - target_x
        ) ** 2
        + (
            projected_y
            - target_y
        ) ** 2
    )

    if len(distance_sq) == 0:
        raise ValueError(
            "No projected vertices are available."
        )

    candidate_count = max(
        1,
        min(
            int(candidate_count),
            len(distance_sq),
        ),
    )

    # Fast partial selection instead of sorting every point.
    if candidate_count < len(distance_sq):
        nearest_indices = np.argpartition(
            distance_sq,
            candidate_count - 1,
        )[:candidate_count]
    else:
        nearest_indices = np.arange(
            len(distance_sq)
        )

    nearest_distances = np.sqrt(
        distance_sq[
            nearest_indices
        ]
    )

    min_distance = float(
        nearest_distances.min()
    )

    # Stay spatially close to Gemini's 2D mark. If the exact region is sparse,
    # automatically widen only enough to include the nearest sample.
    allowed_distance = max(
        float(surface_band_pixels),
        min_distance + 1.5,
    )

    local_mask = (
        nearest_distances
        <= allowed_distance
    )

    local_indices = nearest_indices[
        local_mask
    ]

    if len(local_indices) == 0:
        local_indices = np.array(
            [
                nearest_indices[
                    int(
                        np.argmin(
                            nearest_distances
                        )
                    )
                ]
            ],
            dtype=np.int64,
        )

    # The renderer draws low depth first and high depth last, therefore the
    # largest normalized depth is the visible/front-most point at that pixel.
    best_local = int(
        np.argmax(
            normalized_depth[
                local_indices
            ]
        )
    )

    best_index = int(
        local_indices[
            best_local
        ]
    )

    anchor = projection[
        "original_vertices"
    ][best_index]

    matched_x = float(
        projected_x[
            best_index
        ]
    )

    matched_y = float(
        projected_y[
            best_index
        ]
    )

    pixel_distance = float(
        np.sqrt(
            distance_sq[
                best_index
            ]
        )
    )

    return {
        "anchor_x": float(
            anchor[0]
        ),
        "anchor_y": float(
            anchor[1]
        ),
        "anchor_z": float(
            anchor[2]
        ),
        "pixel_x": float(
            target_x
        ),
        "pixel_y": float(
            target_y
        ),
        "matched_pixel_x": matched_x,
        "matched_pixel_y": matched_y,
        "pixel_distance": pixel_distance,
        "normalized_depth": float(
            normalized_depth[
                best_index
            ]
        ),
        "view": view.strip().lower(),
        "image_size": int(
            image_size
        ),
    }


# =========================================================
# Point-cloud CPU renderer
# =========================================================

def render_points_cpu(
    file_path: str,
    output_path: str,
    view: str = "front",
    image_size: int = 384,
    max_points: int = 30000,
):
    """
    Lightweight CPU point renderer.

    No:
    - OpenGL
    - EGL
    - X11
    - GPU
    - mesh concatenation
    - triangle rasterization

    Suitable for Cloud Run diagnostics
    and AI preview generation.
    """

    selected_view = (
        view.strip().lower()
    )

    if selected_view not in (
        VIEW_CONFIGS
    ):
        raise ValueError(
            f"Unsupported view: "
            f"{selected_view}"
        )

    # -----------------------------------------------------
    # 1. Load scene
    # -----------------------------------------------------

    scene = load_glb_scene(
        file_path
    )

    # -----------------------------------------------------
    # 2. Extract sampled vertices
    # -----------------------------------------------------

    vertices = (
        _extract_vertices(
            scene,
            max_points=max_points,
        )
    )

    if len(vertices) == 0:
        raise ValueError(
            "Model contains "
            "no usable vertices."
        )

    # -----------------------------------------------------
    # 3. Project vertices
    # -----------------------------------------------------

    projection = _project_vertices_for_view(
        vertices=vertices,
        view=selected_view,
        image_size=image_size,
    )

    projected_x = projection[
        "projected_x"
    ]

    projected_y = projection[
        "projected_y"
    ]

    normalized_depth = projection[
        "normalized_depth"
    ]

    center = projection[
        "center"
    ]

    bounds_min = projection[
        "bounds_min"
    ]

    bounds_max = projection[
        "bounds_max"
    ]

    # -----------------------------------------------------
    # 9. Draw back-to-front
    # -----------------------------------------------------

    order = np.argsort(
        normalized_depth
    )

    image = Image.new(
        "RGB",
        (
            image_size,
            image_size,
        ),
        (
            245,
            247,
            250,
        ),
    )

    draw = ImageDraw.Draw(
        image
    )

    # -----------------------------------------------------
    # 10. Draw vertices
    # -----------------------------------------------------

    for index in order:

        x = int(
            projected_x[
                index
            ]
        )

        y = int(
            projected_y[
                index
            ]
        )

        if (
            x < 0
            or x >= image_size
            or y < 0
            or y >= image_size
        ):
            continue

        depth_value = float(
            normalized_depth[
                index
            ]
        )

        # Red/pink anatomical shading
        red = int(
            145
            + depth_value
            * 90
        )

        green = int(
            45
            + depth_value
            * 70
        )

        blue = int(
            55
            + depth_value
            * 70
        )

        red = min(
            240,
            max(
                0,
                red
            ),
        )

        green = min(
            180,
            max(
                0,
                green
            ),
        )

        blue = min(
            190,
            max(
                0,
                blue
            ),
        )

        radius = 1

        draw.ellipse(
            (
                x - radius,
                y - radius,
                x + radius,
                y + radius,
            ),
            fill=(
                red,
                green,
                blue,
            ),
        )

    # -----------------------------------------------------
    # 11. Save PNG
    # -----------------------------------------------------

    output = Path(
        output_path
    )

    output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    image.save(
        output,
        format="PNG",
    )

    # -----------------------------------------------------
    # 12. Return metadata
    # -----------------------------------------------------

    return {
        "view":
            selected_view,

        "file_path":
            str(output),

        "image_size":
            image_size,

        "point_count":
            int(
                len(vertices)
            ),

        "center":
            _vector3(
                center
            ),

        "bounds_min":
            _vector3(
                bounds_min
            ),

        "bounds_max":
            _vector3(
                bounds_max
            ),
    }