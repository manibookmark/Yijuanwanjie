using System;
using UnityEngine;

namespace WanjieScroll.Effects.Attacks
{
    /// <summary>One resident unit's pose and appearance. No targets, damage or group identity.</summary>
    internal interface IPeriodicAttackerVisual : IDisposable
    {
        Vector3 ProjectileOrigin { get; }
        void SetPose(Vector3 position, bool rightward);
        void Tick(float delta, float time);
        void Pulse();
    }
}
