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
        [SerializeField, Tooltip("Spell windup multiplier for the player. Enemies use DifficultySettings.EnemyCastTimeScale")] private float castTimeScale = 0.7f;
        [SerializeField, Tooltip("Sprint drops when running further than this many degrees away from where the camera looks")] private float sprintMaxAimAngle = 60f;

        private bool sprinting;
        private float sprintStartTime;
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
            // The pause menu has the cursor and the clicks; the body stands still meanwhile
            if (PauseMenu.IsPaused)
            {
                parasite.SetInput(new ControlInput { Aim = viewCamera.transform.forward, AimOrigin = viewCamera.transform.position });
                return;
            }

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
            Vector2 stick = moveAction.ReadValue<Vector2>();
            Vector3 move = Vector3.ClampMagnitude(forward * stick.y + right * stick.x, 1f);

            return new ControlInput
            {
                Move = move,
                Aim = cam.forward,
                AimOrigin = cam.position,
                Sprint = UpdateSprint(move, forward),
                JumpPressed = jumpAction.WasPressedThisFrame(),
                AttackPressed = allowButtons && attackAction.WasPressedThisFrame(),
                LeapPressed = allowButtons && leapAction.WasPressedThisFrame(),
                CastSlot = allowButtons ? ReadCastSlot() : 0,
                CastTimeScale = castTimeScale,
            };
        }

        // Sprint is a toggle, like in Risk of Rain. It drops when the body stops, runs too far away from the camera's look,
        // or a cast or a swing goes off (a press on cooldown doesn't count; the parasite's bite doesn't either)
        private bool UpdateSprint(Vector3 move, Vector3 forward)
        {
            if (sprintAction.WasPressedThisFrame())
            {
                sprinting = !sprinting;
                sprintStartTime = Time.time;
            }

            HostBody host = parasite.State == ParasiteState.Controlling ? parasite.Host : null;
            if (move.magnitude <= 0.5f || Vector3.Angle(forward, move) > sprintMaxAimAngle || (host != null && host.LastActionTime >= sprintStartTime))
                sprinting = false;
            return sprinting;
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
