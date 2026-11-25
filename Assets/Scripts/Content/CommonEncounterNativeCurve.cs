using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Content
{
    /// <summary>Private owner of a lazily materialized native curve. Serialized
    /// infinity values are interpreted by JsonUtility, never cast to WrapMode.
    /// JSON can normalize negative zero to positive zero. Only that sign loss
    /// is restored from the immutable author definition; native key getters must
    /// then preserve every float bit, weight and structural field. Other changes
    /// fail explicitly rather than inventing a linear or clamp approximation.
    /// This does not establish cross-engine bit equivalence with the source
    /// game's native interpolation body.</summary>
    internal sealed class CommonEncounterNativeCurve
    {
        [Serializable]
        private sealed class NativeWrapper { public AnimationCurve curve; }
        [Serializable]
        private sealed class SerializedWrapper { public CommonEncounterCurveData curve; }

        private readonly CommonEncounterCurveDefinition _definition;
        private readonly object _gate = new object();
        private AnimationCurve _curve;

        internal CommonEncounterNativeCurve(CommonEncounterCurveDefinition definition)
            => _definition = definition ?? throw new ArgumentNullException(nameof(definition));

        internal float Evaluate(float time)
        {
            if (!CommonEncounterContentCompiler.IsFinite(time))
                throw new ArgumentException("Native curve input must be finite.", nameof(time));
            AnimationCurve curve = GetCurve();
            float value = curve.Evaluate(time);
            if (!CommonEncounterContentCompiler.IsFinite(value))
                throw new InvalidOperationException("Native encounter curve produced a non-finite value.");
            return value;
        }

        private AnimationCurve GetCurve()
        {
            lock (_gate)
            {
                if (_curve != null) return _curve;
                var serialized = new SerializedWrapper { curve = CaptureSerializedData() };
                string originalJson = JsonUtility.ToJson(serialized);
                // Restore only the known JSON sign-zero normalization. Every
                // other float bit and the raw 2/2/4-style structure stay strict.
                SerializedWrapper authorRoundtrip = JsonUtility.FromJson<SerializedWrapper>(originalJson);
                RestoreSerializedNegativeZeros(authorRoundtrip?.curve);
                RequireSame(authorRoundtrip?.curve, "author JSON");
                NativeWrapper native = JsonUtility.FromJson<NativeWrapper>(originalJson);
                if (native?.curve == null)
                    throw new InvalidOperationException("The engine did not deserialize AnimationCurve.");
                RestoreNativeNegativeZeros(native.curve);
                RequireNativeKeys(native.curve.keys);
                // Exercise the actual getters, without interpreting raw infinity
                // integers as their public enum values.
                if (!Enum.IsDefined(typeof(WrapMode), native.curve.preWrapMode) ||
                    !Enum.IsDefined(typeof(WrapMode), native.curve.postWrapMode))
                    throw new InvalidOperationException("Native curve returned an unknown wrap mode.");
                SerializedWrapper roundtrip = JsonUtility.FromJson<SerializedWrapper>(JsonUtility.ToJson(native));
                RestoreSerializedNegativeZeros(roundtrip?.curve);
                RequireSame(roundtrip?.curve, "native JSON roundtrip");
                _curve = native.curve;
                return _curve;
            }
        }

        private CommonEncounterCurveData CaptureSerializedData()
        {
            var result = new CommonEncounterCurveData
            {
                m_PreInfinity = _definition.SerializedPreInfinity,
                m_PostInfinity = _definition.SerializedPostInfinity,
                m_RotationOrder = _definition.RotationOrder,
                m_Curve = new List<CommonEncounterCurveKeyData>()
            };
            foreach (CommonEncounterCurveKeyDefinition key in _definition.Keys)
                result.m_Curve.Add(new CommonEncounterCurveKeyData
                {
                    time = key.Time, value = key.Value,
                    inSlope = key.InSlope, outSlope = key.OutSlope,
                    inWeight = key.InWeight, outWeight = key.OutWeight,
                    weightedMode = key.WeightedMode
                });
            return result;
        }

        private void RestoreSerializedNegativeZeros(CommonEncounterCurveData actual)
        {
            if (actual?.m_Curve == null || actual.m_Curve.Count != _definition.Keys.Count)
                return; // The subsequent complete validation owns shape failures.
            for (int i = 0; i < actual.m_Curve.Count; i++)
            {
                CommonEncounterCurveKeyData key = actual.m_Curve[i];
                if (key == null) continue;
                CommonEncounterCurveKeyDefinition expected = _definition.Keys[i];
                key.time = RestoreNegativeZero(key.time, expected.Time);
                key.value = RestoreNegativeZero(key.value, expected.Value);
                key.inSlope = RestoreNegativeZero(key.inSlope, expected.InSlope);
                key.outSlope = RestoreNegativeZero(key.outSlope, expected.OutSlope);
                key.inWeight = RestoreNegativeZero(key.inWeight, expected.InWeight);
                key.outWeight = RestoreNegativeZero(key.outWeight, expected.OutWeight);
            }
        }

        private void RestoreNativeNegativeZeros(AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            if (keys == null || keys.Length != _definition.Keys.Count)
                return; // RequireNativeKeys will reject the incompatible shape.
            bool restored = false;
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe original = keys[i];
                Keyframe key = original;
                CommonEncounterCurveKeyDefinition expected = _definition.Keys[i];
                key.time = RestoreNegativeZero(key.time, expected.Time);
                key.value = RestoreNegativeZero(key.value, expected.Value);
                key.inTangent = RestoreNegativeZero(key.inTangent, expected.InSlope);
                key.outTangent = RestoreNegativeZero(key.outTangent, expected.OutSlope);
                key.inWeight = RestoreNegativeZero(key.inWeight, expected.InWeight);
                key.outWeight = RestoreNegativeZero(key.outWeight, expected.OutWeight);
                if (Same(key.time, original.time) && Same(key.value, original.value) &&
                    Same(key.inTangent, original.inTangent) && Same(key.outTangent, original.outTangent) &&
                    Same(key.inWeight, original.inWeight) && Same(key.outWeight, original.outWeight))
                    continue;
                keys[i] = key;
                restored = true;
            }
            if (restored) curve.keys = keys;
        }

        private static float RestoreNegativeZero(float actual, float expected)
            => CommonEncounterContentCompiler.FloatBits(expected) == int.MinValue &&
                CommonEncounterContentCompiler.FloatBits(actual) == 0 ? expected : actual;

        private void RequireSame(CommonEncounterCurveData actual, string boundary)
        {
            if (actual?.m_Curve == null || actual.m_Curve.Count != _definition.Keys.Count ||
                actual.m_PreInfinity != _definition.SerializedPreInfinity ||
                actual.m_PostInfinity != _definition.SerializedPostInfinity ||
                actual.m_RotationOrder != _definition.RotationOrder)
                throw new InvalidOperationException("Encounter curve changed serialized structure at " + boundary + ".");
            for (int i = 0; i < actual.m_Curve.Count; i++)
            {
                CommonEncounterCurveKeyData key = actual.m_Curve[i];
                CommonEncounterCurveKeyDefinition expected = _definition.Keys[i];
                if (key == null || key.weightedMode != expected.WeightedMode ||
                    !Same(key.time, expected.Time) || !Same(key.value, expected.Value) ||
                    !Same(key.inSlope, expected.InSlope) || !Same(key.outSlope, expected.OutSlope) ||
                    !Same(key.inWeight, expected.InWeight) || !Same(key.outWeight, expected.OutWeight))
                    throw new InvalidOperationException("Encounter curve key " + i + " changed float bits at " + boundary + ".");
            }
        }

        private void RequireNativeKeys(Keyframe[] keys)
        {
            if (keys == null || keys.Length != _definition.Keys.Count)
                throw new InvalidOperationException("Native curve changed key count.");
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = keys[i];
                CommonEncounterCurveKeyDefinition expected = _definition.Keys[i];
                if ((int)key.weightedMode != expected.WeightedMode ||
                    !Same(key.time, expected.Time) || !Same(key.value, expected.Value) ||
                    !Same(key.inTangent, expected.InSlope) || !Same(key.outTangent, expected.OutSlope) ||
                    !Same(key.inWeight, expected.InWeight) || !Same(key.outWeight, expected.OutWeight))
                    throw new InvalidOperationException("Native curve key " + i + " did not preserve the complete source key.");
            }
        }

        private static bool Same(float left, float right)
            => CommonEncounterContentCompiler.FloatBits(left) == CommonEncounterContentCompiler.FloatBits(right);
    }
}
