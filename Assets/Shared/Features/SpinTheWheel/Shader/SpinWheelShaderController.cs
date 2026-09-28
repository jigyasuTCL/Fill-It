using UnityEngine;

namespace Game.SpinWheel
{
    /// <summary>
    /// Drives the SpinWheelShader material from code, so the number of visual
    /// slices always matches config.Rewards.Count exactly — the same count
    /// SpinWheelUIController's GetTargetAngleForReward() divides 360° by.
    /// Mismatch between these two is the #1 way a wheel visually "lands
    /// wrong", so this keeps them tied to one source of truth.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class SpinWheelShaderController : MonoBehaviour
    {
        private static readonly int SegmentCountId = Shader.PropertyToID("_SegmentCount");
        private static readonly int LineColorId    = Shader.PropertyToID("_LineColor");
        private static readonly int LineWidthId    = Shader.PropertyToID("_LineWidth");
        private static readonly int RotationId     = Shader.PropertyToID("_Rotation");
        private static readonly int MainTexId      = Shader.PropertyToID("_MainTex");

        [SerializeField] private Renderer _wheelRenderer;
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            if (_wheelRenderer == null)
                _wheelRenderer = GetComponent<Renderer>();

            _propBlock = new MaterialPropertyBlock();
        }

        /// <summary>Call once after ApplyRemoteConfig() so segment count matches the reward list.</summary>
        public void SyncSegmentCountToConfig(SpinWheelRemoteConfig config)
        {
            SetSegmentCount(config.RewardTables.Count);
        }

        public void SetSegmentCount(int count)
        {
            _wheelRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(SegmentCountId, count);
            _wheelRenderer.SetPropertyBlock(_propBlock);
        }

        public void SetLineColor(Color color)
        {
            _wheelRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(LineColorId, color);
            _wheelRenderer.SetPropertyBlock(_propBlock);
        }

        public void SetLineWidth(float radians)
        {
            _wheelRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(LineWidthId, radians);
            _wheelRenderer.SetPropertyBlock(_propBlock);
        }

        public void SetTexture(Texture texture)
        {
            _wheelRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetTexture(MainTexId, texture);
            _wheelRenderer.SetPropertyBlock(_propBlock);
        }

        /// <summary>
        /// Optional: drive the wheel's spin visually through the shader's
        /// rotation property instead of rotating the Transform. Only use one
        /// of the two approaches — don't rotate the Transform AND set this,
        /// or the visuals and GetTargetAngleForReward() math will disagree.
        /// </summary>
        public void SetRotationDegrees(float degrees)
        {
            _wheelRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(RotationId, degrees);
            _wheelRenderer.SetPropertyBlock(_propBlock);
        }
    }
}
