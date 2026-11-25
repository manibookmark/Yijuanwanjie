using UnityEngine;
using WanjieScroll.Effects.Map;

namespace WanjieScroll.Effects
{
    public enum BossEntranceSmokeLayer
    {
        BodyFog = 0,
        MainCurrent = 1,
        FlowWisp = 2
    }

    /// <summary>
    /// 五个不规则来向、每向三条松散支流的确定性灵气场。中间支流组成唯一
    /// 的五向主涡流轮廓，两侧宽雾只补体积，少数亮丝只补速度；内圈先汇入
    /// 中心，外圈持续补入，避免整条轨道同步收缩成“鞭子”。
    /// </summary>
    public static class BossEntranceConvergenceParticlePattern
    {
        public const int SourceCount = 5;
        public const int TributariesPerSource = 3;
        public const int StreamCount = SourceCount * TributariesPerSource;
        public const float FlowWispRatio = 0.18f;
        public const float MotionFreezeNormalized = 0.68f;

        public static BossInfinityParticleAppearance GetAppearance(
            BossEntranceEffectPreset preset)
        {
            return new BossInfinityParticleAppearance(
                MonsterSpiritAuraPalette.Deep,
                MonsterSpiritAuraPalette.Core,
                MonsterSpiritAuraPalette.Highlight,
                preset.convergenceOpacity,
                preset.convergenceSmokeSoftness,
                0f,
                smokeBlend: 1f,
                smokeStretch: preset.convergenceSmokeStretch,
                smokeCurl: preset.convergenceSmokeCurl);
        }

        public static BossEntranceSmokeLayer GetLayer(int index)
        {
            if (Hash01(index, 0.619f) < FlowWispRatio)
                return BossEntranceSmokeLayer.FlowWisp;
            return GetStreamIndex(index) % TributariesPerSource == 1
                ? BossEntranceSmokeLayer.MainCurrent
                : BossEntranceSmokeLayer.BodyFog;
        }

        public static float GetVisualVariant(int index)
        {
            switch (GetLayer(index))
            {
                case BossEntranceSmokeLayer.MainCurrent:
                    return 0.5f;
                case BossEntranceSmokeLayer.FlowWisp:
                    return 1f;
                default:
                    return 0f;
            }
        }

        public static int GetStreamIndex(int index)
        {
            return Mathf.Abs(index) % StreamCount;
        }

        public static int GetSourceIndexFromStream(int stream)
        {
            return Mathf.Abs(stream) % StreamCount /
                   TributariesPerSource;
        }

        public static float GetSourceStrengthFromStream(int stream)
        {
            switch (GetSourceIndexFromStream(stream))
            {
                case 0: return 1f;
                case 1: return 0.78f;
                case 2: return 0.92f;
                case 3: return 0.72f;
                default: return 0.84f;
            }
        }

        public static float GetStreamProgress(
            BossEntranceEffectPreset preset,
            int index)
        {
            int count = Mathf.Clamp(
                preset != null ? preset.convergenceParticleCount : 32,
                32,
                320);
            int stream = GetStreamIndex(index);
            int slot = Mathf.Max(0, index) / StreamCount;
            int streamPopulation = Mathf.Max(
                1,
                (count - 1 - stream) / StreamCount + 1);
            if (streamPopulation <= 1)
                return 0f;

            float jitter = Hash01(index, 0.247f) * 0.42f;
            return Mathf.Clamp01(
                (slot + jitter) /
                Mathf.Max(1f, streamPopulation - 0.82f));
        }

        public static float GetOrbitalSpeed(
            BossEntranceEffectPreset preset)
        {
            return GetOrbitalSpeed(
                preset != null ? preset.convergenceSmokeCurl : 0.5f);
        }

        public static float GetOrbitalSpeed(float curl)
        {
            return Mathf.Lerp(0.95f, 1.45f, Mathf.Clamp01(curl));
        }

        public static float GetArrivalTime(
            BossEntranceEffectPreset preset,
            int index)
        {
            float duration = Mathf.Max(0.1f, preset.convergenceDuration);
            float reveal = Mathf.Clamp(
                preset.convergenceRevealNormalized,
                0.5f,
                0.92f);
            float u = GetStreamProgress(preset, index);
            int stream = GetStreamIndex(index);
            int source = GetSourceIndexFromStream(stream);
            BossEntranceSmokeLayer layer = GetLayer(index);
            float sourceOffset = Mathf.Lerp(
                -0.009f,
                0.009f,
                Hash01(source, 0.733f));
            float particleOffset = Mathf.Lerp(
                -0.006f,
                0.006f,
                Hash01(index, 0.183f));
            float arrivalNormalized = Mathf.Lerp(
                reveal - 0.19f,
                reveal - 0.025f,
                Mathf.Pow(u, 0.8f));
            arrivalNormalized += sourceOffset + particleOffset;
            // 宽雾先铺底，主气脉随后完成轮廓，细亮丝最后强调速度。
            switch (layer)
            {
                case BossEntranceSmokeLayer.FlowWisp:
                    arrivalNormalized += 0.006f;
                    break;
                case BossEntranceSmokeLayer.MainCurrent:
                    break;
                default:
                    arrivalNormalized -= 0.008f;
                    break;
            }
            arrivalNormalized = Mathf.Clamp(
                arrivalNormalized,
                0.42f,
                0.9f);
            return duration * arrivalNormalized;
        }

        public static BossEntranceParticleSeed GetSeed(
            BossEntranceEffectPreset preset,
            int index)
        {
            float n1 = Hash01(index, 0.183f);
            float n2 = Hash01(index, 0.479f);
            float n3 = Hash01(index, 0.821f);
            float n4 = Hash01(index, 0.357f);
            float n5 = Hash01(index, 0.947f);
            BossEntranceSmokeLayer layer = GetLayer(index);
            bool flowWisp = layer == BossEntranceSmokeLayer.FlowWisp;
            bool mainCurrent = layer == BossEntranceSmokeLayer.MainCurrent;
            int stream = GetStreamIndex(index);
            int source = GetSourceIndexFromStream(stream);
            int tributary = stream % TributariesPerSource;
            float streamProgress = GetStreamProgress(preset, index);

            float easedRadius = Mathf.SmoothStep(0f, 1f, streamProgress);
            float radius = Mathf.Lerp(0.16f, 1.28f, easedRadius) *
                           preset.convergenceRadius;
            radius *= flowWisp
                ? Mathf.Lerp(0.96f, 1.04f, n1)
                : mainCurrent
                    ? Mathf.Lerp(0.95f, 1.05f, n1)
                    : Mathf.Lerp(0.91f, 1.09f, n1);

            float baseAngle = GetSourceAngle(source);
            float spiralSweep = Mathf.Lerp(
                0.78f,
                1.28f,
                Mathf.Clamp01(preset.convergenceSmokeCurl)) *
                GetSourceSweep(source);
            float branchEnvelope = Mathf.Sin(
                Mathf.Clamp01(streamProgress) * Mathf.PI);
            float branchOffset = (tributary - 1f) * 0.078f *
                                 branchEnvelope;
            float continuousSway = Mathf.Sin(
                streamProgress * Mathf.PI * 2f + source * 1.17f) *
                0.015f;
            float scatter = flowWisp
                ? Mathf.Lerp(-0.04f, 0.04f, n2)
                : mainCurrent
                    ? Mathf.Lerp(-0.025f, 0.025f, n2)
                    : Mathf.Lerp(-0.11f, 0.11f, n2);
            float angle = baseAngle - spiralSweep * streamProgress +
                          branchOffset + continuousSway + scatter;
            Vector2 start = new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius);

            float arrivalTime = GetArrivalTime(preset, index);
            float orbitalSpeed = GetOrbitalSpeed(preset);
            Vector2 velocity = CalculateCenteringVelocity(
                start,
                arrivalTime,
                orbitalSpeed);
            float lifetime = arrivalTime / MotionFreezeNormalized;
            float sourceStrength = GetSourceStrengthFromStream(stream);
            float size = preset.convergenceParticleSize *
                         (flowWisp
                             ? Mathf.Lerp(0.32f, 0.72f, n3)
                             : mainCurrent
                                 ? Mathf.Lerp(0.82f, 1.46f, n3)
                                 : Mathf.Lerp(0.68f, 1.90f, n3)) *
                         Mathf.Lerp(0.84f, 1.06f, sourceStrength);
            float particleAlpha = flowWisp
                ? Mathf.Lerp(0.74f, 0.98f, n4)
                : mainCurrent
                    ? Mathf.Lerp(0.68f, 0.92f, n4)
                    : Mathf.Lerp(0.46f, 0.76f, n4);
            Color color = new Color(
                1f,
                1f,
                1f,
                particleAlpha * sourceStrength);
            Vector2 initialDirection = velocity +
                orbitalSpeed * Perpendicular(start);
            float rotation = Mathf.Atan2(
                                 initialDirection.y,
                                 initialDirection.x) *
                             Mathf.Rad2Deg +
                             (flowWisp
                                 ? Mathf.Lerp(-7f, 7f, n5)
                                 : mainCurrent
                                     ? Mathf.Lerp(-12f, 12f, n5)
                                     : Mathf.Lerp(-25f, 25f, n5));
            return new BossEntranceParticleSeed(
                start,
                velocity,
                lifetime,
                size,
                rotation,
                color);
        }

        /// <summary>采样与 Infinity orbitalZ 相同的解析轨迹。</summary>
        public static Vector2 SamplePosition(
            BossEntranceEffectPreset preset,
            int index,
            float elapsed)
        {
            BossEntranceParticleSeed seed = GetSeed(preset, index);
            float arrival = GetArrivalTime(preset, index);
            SampleState(
                preset,
                seed,
                arrival,
                elapsed,
                out Vector2 position,
                out _);
            return position;
        }

        public static Vector2 SampleVelocity(
            BossEntranceEffectPreset preset,
            int index,
            float elapsed)
        {
            float arrival = GetArrivalTime(preset, index);
            BossEntranceParticleSeed seed = GetSeed(preset, index);
            SampleState(
                preset,
                seed,
                arrival,
                elapsed,
                out _,
                out Vector2 velocity);
            return velocity;
        }

        public static void SampleState(
            BossEntranceEffectPreset preset,
            BossEntranceParticleSeed seed,
            float arrival,
            float elapsed,
            out Vector2 position,
            out Vector2 velocity)
        {
            if (elapsed >= arrival)
            {
                position = Vector2.zero;
                velocity = Vector2.zero;
                return;
            }

            float orbitalSpeed = GetOrbitalSpeed(preset);
            float time = Mathf.Clamp(elapsed, 0f, arrival);
            IntegrateOrbital(
                seed.PositionUnit,
                seed.VelocityUnit,
                orbitalSpeed,
                time,
                out position,
                out velocity);
        }

        private static Vector2 CalculateCenteringVelocity(
            Vector2 start,
            float travelTime,
            float orbitalSpeed)
        {
            if (Mathf.Abs(orbitalSpeed) < 0.0001f)
                return -start / Mathf.Max(0.001f, travelTime);

            float halfAngle = orbitalSpeed * travelTime * 0.5f;
            float denominator = 2f * Mathf.Sin(halfAngle);
            if (Mathf.Abs(denominator) < 0.0001f)
                return -start / Mathf.Max(0.001f, travelTime);
            return -orbitalSpeed / denominator *
                   Rotate(start, halfAngle);
        }

        private static void IntegrateOrbital(
            Vector2 start,
            Vector2 velocity,
            float orbitalSpeed,
            float time,
            out Vector2 position,
            out Vector2 currentVelocity)
        {
            if (Mathf.Abs(orbitalSpeed) < 0.0001f)
            {
                position = start + velocity * time;
                currentVelocity = velocity;
                return;
            }

            float angle = orbitalSpeed * time;
            float sin = Mathf.Sin(angle);
            float cos = Mathf.Cos(angle);
            Vector2 rotatedStart = new Vector2(
                start.x * cos - start.y * sin,
                start.x * sin + start.y * cos);
            position = rotatedStart +
                       velocity * (sin / orbitalSpeed) +
                       Perpendicular(velocity) *
                       ((1f - cos) / orbitalSpeed);
            currentVelocity = velocity +
                              orbitalSpeed * Perpendicular(position);
        }

        private static float GetSourceAngle(int source)
        {
            switch (Mathf.Abs(source) % SourceCount)
            {
                case 0: return 0.08f;
                case 1: return 1.40f;
                case 2: return 2.55f;
                case 3: return 3.88f;
                default: return 5.33f;
            }
        }

        private static float GetSourceSweep(int source)
        {
            switch (Mathf.Abs(source) % SourceCount)
            {
                case 0: return 1f;
                case 1: return 0.76f;
                case 2: return 1.16f;
                case 3: return 0.84f;
                default: return 1.08f;
            }
        }

        private static Vector2 Rotate(Vector2 value, float radians)
        {
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(
                value.x * cos - value.y * sin,
                value.x * sin + value.y * cos);
        }

        private static Vector2 Perpendicular(Vector2 value)
        {
            return new Vector2(-value.y, value.x);
        }

        private static float Hash01(int index, float salt)
        {
            return Mathf.Repeat(
                Mathf.Sin((index + 1) * (17.123f + salt * 37.719f)) *
                24634.6345f,
                1f);
        }
    }
}
