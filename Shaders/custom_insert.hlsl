// =============================================================================
// DennokoEx custom_insert.hlsl - lilToon 2.x Include / Structure Extension
// =============================================================================

// Ensure TBN matrix is passed to the fragment shader (used for decal normals and 2nd reflection anisotropy)
#define LIL_V2F_FORCE_TANGENT
#define LIL_V2F_FORCE_BITANGENT
