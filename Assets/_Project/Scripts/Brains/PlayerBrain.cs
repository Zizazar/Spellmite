using UnityEngine;
using UnityEngine.InputSystem;

namespace Spellmite
{
    // Reads player input and drives the parasite. The parasite forwards it to whatever host it controls,
    // so the same buttons mean the same thing in every body.
    [DefaultExecutionOrder(-50)]
    public class PlayerBrain : MonoBehaviour
    {
        [SerializeField] private ParasiteBody parasite;
        [SerializeField] private Camera viewCamera;

        private InputAction moveAction;
        private InputAction jumpAction;
        private InputAction attackAction;
        private InputAction leapAction;
        private InputAction sprintAction;
        private readonly InputAction[] castActions = new InputAction[4];

        private void Awake()
        {
            var actions = InputSystem.actions;
            moveAction = actions.FindAction("Player/Move", true);
            jumpAction = actions.FindAction("Player/Jump", true);
            attackAction = actions.FindAction("Player/Attack", true);
            leapAction = actions.FindAction("Player/Leap", true);
            sprintAction = actions.FindAction("Player/Sprint", true);
            for (int i = 0; i < castActions.Length; i++)
                castActions[i] = actions.FindAction($"Player/Cast{i + 1}", true);
            actions.FindActionMap("Player", true).Enable();
        }

        private void Update()
        {
            // Cursor lock only works from a user input event on WebGL: lock on click and swallow that click
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                parasite.SetInput(ReadInput(false));
                return;
            }

            parasite.SetInput(ReadInput(true));
        }

        private ControlInput ReadInput(bool allowButtons)
        {
            Transform cam = viewCamera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector2 move = moveAction.ReadValue<Vector2>();

            return new ControlInput
            {
                Move = Vector3.ClampMagnitude(forward * move.y + right * move.x, 1f),
                Aim = cam.forward,
                AimOrigin = cam.position,
                Sprint = sprintAction.IsPressed(),
                JumpPressed = jumpAction.WasPressedThisFrame(),
                AttackPressed = allowButtons && attackAction.WasPressedThisFrame(),
                LeapPressed = allowButtons && leapAction.WasPressedThisFrame(),
                CastSlot = allowButtons ? ReadCastSlot() : 0,
            };
        }

        // 1-based slot of the cast key pressed this frame, 0 = none
        private int ReadCastSlot()
        {
            for (int i = 0; i < castActions.Length; i++)
                if (castActions[i].WasPressedThisFrame()) return i + 1;
            return 0;
        }
    }
}
