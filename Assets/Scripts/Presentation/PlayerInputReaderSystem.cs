using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameHolder.PureDots
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class PlayerInputReaderSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<SimulationInput>();
        }

        protected override void OnUpdate()
        {
            float2 input = float2.zero;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1.0f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1.0f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1.0f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1.0f;
            }

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.leftStick.ReadValue();
                if (stick.sqrMagnitude > PresentationConstants.GamepadDeadZoneSq)
                {
                    input = new float2(stick.x, stick.y);
                }
            }

            var simulationInput = new SimulationInput { Movement = input };
            var camera = GamePresentationBootstrap.Instance != null ? GamePresentationBootstrap.Instance.GameCamera : Camera.main;
            if (camera != null && camera.orthographic && SystemAPI.HasSingleton<SimulationSnapshot>())
            {
                var origin = SystemAPI.GetSingleton<SimulationSnapshot>().WorldOrigin;
                // Viewport corners support zoom, aspect changes, camera lag and rotation without engine objects in jobs.
                var bottom = camera.ViewportToWorldPoint(new Vector3(0, 0, -camera.transform.position.z));
                var top = camera.ViewportToWorldPoint(new Vector3(1, 1, -camera.transform.position.z));
                var left = camera.ViewportToWorldPoint(new Vector3(0, 1, -camera.transform.position.z));
                var right = camera.ViewportToWorldPoint(new Vector3(1, 0, -camera.transform.position.z));
                var min = Vector2.Min(Vector2.Min(bottom, top), Vector2.Min(left, right));
                var max = Vector2.Max(Vector2.Max(bottom, top), Vector2.Max(left, right));
                simulationInput.ViewCenterWorld = origin + (double2)(float2)((min + max) * .5f);
                simulationInput.ViewHalfSize = (max - min) * .5f;
                simulationInput.ViewValid = 1;
            }
            SystemAPI.SetSingleton(simulationInput);
        }
    }
}
