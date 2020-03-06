using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(Camera))]
public class RemainableAfterImagePostEffect : MonoBehaviour
{
    private enum RemainableAfterImageEffectMode
    {
        Intermittent = 0,
        Continuous
    }

    [SerializeField] private RemainableAfterImageEffectMode _mode;
    [SerializeField] private AROcclusionManager _occlusionManager;
    [SerializeField] private Mesh _quadMesh;
    [SerializeField] private Shader _shader;
    [SerializeField] private Shader _shaderForCurrentFrame;

    private const float DISTANCE_FROM_CAMERA = 1f;

    private Camera _camera;
    private Transform _anchor;
    private CommandBuffer _commandBuffer;
    private RenderTexture _cameraFeedBuffer;
    private Texture2D _humanStencilTexture;
    private Material _materialForCurrentFrame;
    private readonly List<CommandBuffer> _currentCommandBuffers = new List<CommandBuffer>();

    #region Only Use Continuous Mode

    private const int NUM_OF_FRAME_TO_SKIP = 8;
    private bool _isOn;

    #endregion

    private int PropertyID_UVMultiplierLandScape;
    private int PropertyID_UVMultiplierPortrait;
    private int PropertyID_UVFlip;
    private int PropertyID_OnWide;
    private int PropertyID_StencilTex;
    private int PropertyID_CameraFeedTexture;

    private void Awake()
    {
        _camera = GetComponent<Camera>();

        // The point to refer when draw mesh. Its position is DISTANCE_FROM_CAMERA forward from camera in any time
        _anchor = new GameObject("Draw Mesh Anchor").transform;
        _anchor.SetParent(_camera.transform);
        _anchor.localPosition = new Vector3(0, 0, DISTANCE_FROM_CAMERA);

        PropertyID_UVMultiplierLandScape = Shader.PropertyToID("_UVMultiplierLandScape");
        PropertyID_UVMultiplierPortrait = Shader.PropertyToID("_UVMultiplierPortrait");
        PropertyID_UVFlip = Shader.PropertyToID("_UVFlip");
        PropertyID_OnWide = Shader.PropertyToID("_OnWide");
        PropertyID_StencilTex = Shader.PropertyToID("_StencilTex");
        PropertyID_CameraFeedTexture = Shader.PropertyToID("_CameraFeedTexture");

        // Create CommandBuffer that copies latest CameraFeed to RenderTexture buffer.
        // The reason why CameraEvent is AfterForwardOpaque is just because CameraFeed is rendered in CameraEvent.BeforeForwardOpaque in ARCameraBackground script (for ForwardRendering)
        // So you could change CameraEvent if you want
        _cameraFeedBuffer = new RenderTexture(_camera.pixelWidth, _camera.pixelHeight, 0);
        _commandBuffer = new CommandBuffer();
        _commandBuffer.Blit(null, _cameraFeedBuffer);
        _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);

        _materialForCurrentFrame = new Material(_shaderForCurrentFrame);
        _materialForCurrentFrame.SetTexture(PropertyID_CameraFeedTexture, _cameraFeedBuffer);
    }

    private void Update()
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                switch (_mode)
                {
                    case RemainableAfterImageEffectMode.Intermittent:
                        SetCommandToDraw();
                        break;
                    case RemainableAfterImageEffectMode.Continuous:
                        _isOn = true;
                        StartCoroutine(CreateAfterImageContinuously());
                        break;
                }
            }

            if (touch.phase == TouchPhase.Ended)
            {
                switch (_mode)
                {
                    case RemainableAfterImageEffectMode.Continuous:
                        _isOn = false;
                        break;
                }
            }
        }
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        _humanStencilTexture = _occlusionManager.humanStencilTexture;
        if (_humanStencilTexture)
        {
            // When device orientation changes
            if (_cameraFeedBuffer.width != _camera.pixelWidth)
            {
                ReInitCameraFeedBuffer();
            }

            // Render HumanSegmentationImage of current frame so that it won't be hidden by created after-images 
            SetMaterialProperty(_materialForCurrentFrame, false);
            Graphics.Blit(src, dest, _materialForCurrentFrame);
        }
        else
        {
            Graphics.Blit(src, dest);
        }
    }

    private void SetCommandToDraw()
    {
        var command = new CommandBuffer();
        var material = new Material(_shader);
        var cameraFeed = new RenderTexture(_camera.pixelWidth, _camera.pixelHeight, 0);

        // Copy CameraFeed
        Graphics.Blit(_cameraFeedBuffer, cameraFeed);
        material.mainTexture = cameraFeed;
        SetMaterialProperty(material);

        // Draw Quad mesh that renders HumanSegmentationImage in the anchor's world position and rotation, and scale it to exactly same as screen size
        command.DrawMesh(
            _quadMesh,
            Matrix4x4.TRS(_anchor.position, _anchor.rotation, GetMeshScale()),
            material, 0, 0);

        // CameraEvent can be anywhere after CameraEvent.BeforeForwardOpaque when CameraFeed renders on background
        _camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, command);
        _currentCommandBuffers.Add(command);
    }

    private IEnumerator CreateAfterImageContinuously()
    {
        while (_isOn)
        {
            SetCommandToDraw();
            for (int i = 0; i < NUM_OF_FRAME_TO_SKIP; i++)
            {
                yield return null;
            }
        }
    }

    private Vector3 GetMeshScale()
    {
        // Calculate mesh height that can be seen as same size as screen in the point of the anchor 
        var pointTop = _camera.ScreenToWorldPoint(new Vector3(0, 0, DISTANCE_FROM_CAMERA));
        var pointBottom = _camera.ScreenToWorldPoint(new Vector3(0, _camera.pixelHeight, DISTANCE_FROM_CAMERA));
        var frustumHeight = Vector3.Distance(pointTop, pointBottom);
        return new Vector3(frustumHeight * _camera.aspect, frustumHeight, 1);
    }

    private void SetMaterialProperty(Material mat, bool shouldCopy = true)
    {
        if (_humanStencilTexture)
        {
            // Those property are to adjust UV of HumanStencilTexture in any orientation and resolution
            if (Input.deviceOrientation == DeviceOrientation.LandscapeRight)
            {
                mat.SetFloat(PropertyID_UVMultiplierLandScape, CalculateUVMultiplierLandScape(_humanStencilTexture));
                mat.SetFloat(PropertyID_UVFlip, 0);
                mat.SetInt(PropertyID_OnWide, 1);
            }
            else if (Input.deviceOrientation == DeviceOrientation.LandscapeLeft)
            {
                mat.SetFloat(PropertyID_UVMultiplierLandScape, CalculateUVMultiplierLandScape(_humanStencilTexture));
                mat.SetFloat(PropertyID_UVFlip, 1);
                mat.SetInt(PropertyID_OnWide, 1);
            }
            else
            {
                mat.SetFloat(PropertyID_UVMultiplierPortrait, CalculateUVMultiplierPortrait(_humanStencilTexture));
                mat.SetInt(PropertyID_OnWide, 0);
            }

            if (shouldCopy)
            {
                // For remained after-image
                var humanStencil = new RenderTexture(_humanStencilTexture.width, _humanStencilTexture.height, 0);
                Graphics.Blit(_humanStencilTexture, humanStencil);
                mat.SetTexture(PropertyID_StencilTex, humanStencil);
            }
            else
            {
                // For after-image of current frame
                mat.SetTexture(PropertyID_StencilTex, _humanStencilTexture);
            }
        }
    }

    private float CalculateUVMultiplierLandScape(Texture2D cameraTexture)
    {
        float screenAspect = (float) Screen.width / Screen.height;
        float cameraTextureAspect = (float) cameraTexture.width / cameraTexture.height;
        return screenAspect / cameraTextureAspect;
    }

    private float CalculateUVMultiplierPortrait(Texture2D cameraTexture)
    {
        float screenAspect = (float) Screen.height / Screen.width;
        float cameraTextureAspect = (float) cameraTexture.width / cameraTexture.height;
        return screenAspect / cameraTextureAspect;
    }

    public void Clear()
    {
        _isOn = false;
        StopCoroutine(CreateAfterImageContinuously());
        foreach (var command in _currentCommandBuffers)
        {
            _camera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, command);
        }

        _currentCommandBuffers.Clear();
        Resources.UnloadUnusedAssets();
    }

    public void ChangeMode(Dropdown dropdown)
    {
        Clear();
        _mode = (RemainableAfterImageEffectMode) Enum.ToObject(typeof(RemainableAfterImageEffectMode), dropdown.value);
    }

    private void ReInitCameraFeedBuffer()
    {
        // DeInit CommandBuffer and CameraFeedBuffer
        _commandBuffer.Clear();
        _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);
        _cameraFeedBuffer.Release();

        // Create CommandBuffer and CameraFeedBuffer again
        _cameraFeedBuffer = new RenderTexture(_camera.pixelWidth, _camera.pixelHeight, 0);
        _commandBuffer.Blit(null, _cameraFeedBuffer);
        _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);

        _materialForCurrentFrame.SetTexture(PropertyID_CameraFeedTexture, _cameraFeedBuffer);
    }
}