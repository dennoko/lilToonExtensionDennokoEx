Shader "dennokoworks/DennokoEx/CompatibilityFixture"
{
    Properties
    {
        _CustomMaskPacked ("Packed", 2D) = "white" {}
        _CustomRefl2ndMaskTex ("R", 2D) = "white" {}
        _CustomRim2ndMaskTex ("G", 2D) = "white" {}
        _CustomNormal3rdMaskTex ("B", 2D) = "white" {}
        _CustomMain4thMaskTex ("A", 2D) = "white" {}
    }
    SubShader { Pass { Color (1,1,1,1) } }
}
