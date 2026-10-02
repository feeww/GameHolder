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
                if (stick.sqrMagnitude > 0.04f)
                {
                    input = new float2(stick.x, stick.y);
                }
            }

            SystemAPI.SetSingleton(new SimulationInput { Movement = input });
        }
    }
}
