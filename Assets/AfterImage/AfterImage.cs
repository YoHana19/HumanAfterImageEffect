using UnityEngine;

public class AfterImage
{
    private readonly Camera _camera;
    private readonly Material _material;

    private readonly int PropertyID_UVMultiplierLandScape;
    private readonly int PropertyID_UVMultiplierPortrait;
    private readonly int PropertyID_UVFlip;
    private readonly int PropertyID_OnWide;
    private readonly int PropertyID_StencilTex;

    public AfterImage(Camera camera, Material material)
    {
        _camera = camera;
        _material = material;

        PropertyID_UVMultiplierLandScape = Shader.PropertyToID("_UVMultiplierLandScape");
        PropertyID_UVMultiplierPortrait = Shader.PropertyToID("_UVMultiplierPortrait");
        PropertyID_UVFlip = Shader.PropertyToID("_UVFlip");
        PropertyID_OnWide = Shader.PropertyToID("_OnWide");
        PropertyID_StencilTex = Shader.PropertyToID("_StencilTex");
    }

    public void Draw(Texture cameraFeed)
    {
        _material.mainTexture = cameraFeed;
        // Draw after-image the size of which is corresponding to screen
        Graphics.DrawTexture(new Rect(0, 0, _camera.pixelWidth, _camera.pixelHeight), cameraFeed, _material);
    }

    public void SetMaterialProperty(Texture humanStencilTexture)
    {
        // Those property are to adjust UV of HumanStencilTexture in any orientation and resolution
        if (Input.deviceOrientation == DeviceOrientation.LandscapeRight)
        {
            _material.SetFloat(PropertyID_UVMultiplierLandScape, CalculateUVMultiplierLandScape(humanStencilTexture));
            _material.SetFloat(PropertyID_UVFlip, 0);
            _material.SetInt(PropertyID_OnWide, 1);
        }
        else if (Input.deviceOrientation == DeviceOrientation.LandscapeLeft)
        {
            _material.SetFloat(PropertyID_UVMultiplierLandScape, CalculateUVMultiplierLandScape(humanStencilTexture));
            _material.SetFloat(PropertyID_UVFlip, 1);
            _material.SetInt(PropertyID_OnWide, 1);
        }
        else
        {
            _material.SetFloat(PropertyID_UVMultiplierPortrait, CalculateUVMultiplierPortrait(humanStencilTexture));
            _material.SetInt(PropertyID_OnWide, 0);
        }

        // Set HumanStencilTexture to material
        _material.SetTexture(PropertyID_StencilTex, humanStencilTexture);
    }

    private float CalculateUVMultiplierLandScape(Texture textureFromAROcclusionManager)
    {
        float screenAspect = (float) Screen.width / Screen.height;
        float cameraTextureAspect = (float) textureFromAROcclusionManager.width / textureFromAROcclusionManager.height;
        return screenAspect / cameraTextureAspect;
    }

    private float CalculateUVMultiplierPortrait(Texture textureFromAROcclusionManager)
    {
        float screenAspect = (float) Screen.height / Screen.width;
        float cameraTextureAspect = (float) textureFromAROcclusionManager.width / textureFromAROcclusionManager.height;
        return screenAspect / cameraTextureAspect;
    }
}
