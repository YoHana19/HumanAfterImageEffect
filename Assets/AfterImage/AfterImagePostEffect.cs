using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(Camera))]
public class AfterImagePostEffect : MonoBehaviour
{
    [SerializeField] private AROcclusionManager _occlusionManager;
    [SerializeField] private Shader _shader;

    private const int NUM_OF_IMAGES = 10;
    private const int FRAME_OF_INTERVAL = 4;

    private readonly (int, int)[] _humanStencilTextureResolution =
    {
        (256, 192), // Fastest
        (960, 720), // Medium
        (1920, 1440) // Best
    };

    private readonly List<AfterImage> _afterImages = new List<AfterImage>();
    private readonly List<RenderTexture> _cameraFeedBuffers = new List<RenderTexture>();
    private readonly List<RenderTexture> _stencilBuffers = new List<RenderTexture>();

    private Camera _camera;
    private CommandBuffer _commandBuffer;

    private void Awake()
    {
        _camera = GetComponent<Camera>();

        // Create instances to draw after-image
        for (int i = 0; i < NUM_OF_IMAGES; i++)
        {
            _afterImages.Add(new AfterImage(_camera, new Material(_shader)));
        }

        var resolution = (0, 0);
        switch (_occlusionManager.humanSegmentationStencilMode)
        {
            case SegmentationStencilMode.Fastest:
                resolution = _humanStencilTextureResolution[0];
                break;
            case SegmentationStencilMode.Medium:
                resolution = _humanStencilTextureResolution[1];
                break;
            case SegmentationStencilMode.Best:
                resolution = _humanStencilTextureResolution[2];
                break;
        }

        // Create buffer of RenderTextures to copy CameraFeed and HumanStencilTexture of each frames
        for (int i = 0; i < (NUM_OF_IMAGES - 1) * FRAME_OF_INTERVAL + 1; i++)
        {
            _cameraFeedBuffers.Add(new RenderTexture(_camera.pixelWidth, _camera.pixelHeight, 0));
            _stencilBuffers.Add(new RenderTexture(resolution.Item1, resolution.Item2, 0));
        }

        // Create CommandBuffer that copies latest CameraFeed to last RenderTexture in buffer.
        // The reason why CameraEvent is AfterForwardOpaque is just because CameraFeed is rendered in CameraEvent.BeforeForwardOpaque in ARCameraBackground script (for ForwardRendering)
        // So you could change CameraEvent if you want
        _commandBuffer = new CommandBuffer();
        _commandBuffer.Blit(null, _cameraFeedBuffers.Last());
        _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);
    }

    private void Update()
    {
        // Update CameraFeed buffer except last one of the buffer in every frame
        // This should be called before the CommandBuffer executes
        for (int i = 0; i < _cameraFeedBuffers.Count - 1; i++)
        {
            Graphics.Blit(_cameraFeedBuffers[i + 1], _cameraFeedBuffers[i]);
        }
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        var humanStencil = _occlusionManager.humanStencilTexture;
        if (humanStencil)
        {
            // When device orientation changes
            if (_cameraFeedBuffers.Last().width != _camera.pixelWidth)
            {
                ReInitCameraFeedBuffers();
            }

            // Update stencil buffer in every frame
            for (int i = 0; i < _stencilBuffers.Count - 1; i++)
            {
                Graphics.Blit(_stencilBuffers[i + 1], _stencilBuffers[i]);
            }

            Graphics.Blit(humanStencil, _stencilBuffers.Last());

            // Update HumanStencilTexture of material property in every frame
            for (int i = 0; i < _afterImages.Count; i++)
            {
                _afterImages[i].SetMaterialProperty(_stencilBuffers[i * FRAME_OF_INTERVAL]);
            }
        }

        Graphics.Blit(src, dest);
    }

    private void OnGUI()
    {
        if (Event.current.type.Equals(EventType.Repaint))
        {
            for (int i = 0; i < _afterImages.Count; i++)
            {
                // Draw after-image
                // This is right place to call Graphics.DrawTexture
                _afterImages[i].Draw(_cameraFeedBuffers[i * FRAME_OF_INTERVAL]);
            }
        }
    }

    private void ReInitCameraFeedBuffers()
    {
        // DeInit CommandBuffer and CameraFeed buffer
        _commandBuffer.Clear();
        _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);
        var total = _cameraFeedBuffers.Count;
        foreach (var cameraFeed in _cameraFeedBuffers)
        {
            cameraFeed.Release();
        }

        _cameraFeedBuffers.Clear();

        // Create CommandBuffer and CameraFeed buffer again
        for (int i = 0; i < total; i++)
        {
            _cameraFeedBuffers.Add(new RenderTexture(_camera.pixelWidth, _camera.pixelHeight, 0));
        }

        _commandBuffer.Blit(null, _cameraFeedBuffers.Last());
        _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);
    }
}
