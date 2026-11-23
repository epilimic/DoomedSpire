#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ManagedDoom;
using ManagedDoom.UserInput;

namespace DoomedSpire.Host;

/// <summary>
/// The player's keyboard and mouse as the engine sees it. The popup tells it which keys and mouse buttons are down and how far the mouse moved;
/// every game tic the engine asks it for the player's command (move, turn, fire, use, weapon). Based on the original frontend's
/// SilkUserInput.BuildTicCmd, with the same default keys (Up/W forward, Down/S back, A/D strafe, Left/Right turn, Ctrl or left click fire,
/// Space or right click use, Shift run, 1-7 weapons).
/// The mouse only turns the player left and right. Unlike the original it never walks or strafes the player (no forward/back from mouse movement,
/// no strafing with the strafe key held): the owner wants WASD for walking. Godot-free (the tests use it).
/// </summary>
internal sealed class PlayerInput : IUserInput
{
	private readonly Config config;
	private readonly bool[] down = new bool[(int)DoomKey.Count];
	private readonly bool[] mouseDown = new bool[(int)DoomMouseButton.Count];
	private readonly bool[] weaponKeys = new bool[7];
	private int turnHeld;
	private double mouseTurn;
	private double mouseRemainder;

	public PlayerInput(Config config)
	{
		this.config = config;
	}

	public void SetKey(DoomKey key, bool pressed)
	{
		if (key >= 0 && key < DoomKey.Count)
		{
			down[(int)key] = pressed;
		}
	}

	public void SetMouseButton(DoomMouseButton button, bool pressed)
	{
		if (button >= 0 && button < DoomMouseButton.Count)
		{
			mouseDown[(int)button] = pressed;
		}
	}

	/// <summary>The mouse moved sideways by <paramref name="pixels"/> (right is positive). Collected until the next game tic, which turns the player by it.</summary>
	public void AddMouseTurn(double pixels)
	{
		mouseTurn += pixels;
	}

	/// <summary>Lets go of every key and mouse button and forgets any mouse movement not yet used.</summary>
	public void ReleaseAll()
	{
		Array.Clear(down);
		Array.Clear(mouseDown);
		mouseTurn = 0;
		mouseRemainder = 0;
	}

	public bool IsAnythingDown() => down.Any(d => d) || mouseDown.Any(d => d);

	public IEnumerable<DoomKey> PressedKeys()
	{
		for (var i = 0; i < down.Length; i++)
		{
			if (down[i])
			{
				yield return (DoomKey)i;
			}
		}
	}

	private bool IsPressed(KeyBinding binding)
	{
		foreach (var key in binding.Keys)
		{
			if (down[(int)key])
			{
				return true;
			}
		}
		foreach (var button in binding.MouseButtons)
		{
			if (mouseDown[(int)button])
			{
				return true;
			}
		}
		return false;
	}

	public void BuildTicCmd(TicCmd cmd)
	{
		var keyForward = IsPressed(config.key_forward);
		var keyBackward = IsPressed(config.key_backward);
		var keyStrafeLeft = IsPressed(config.key_strafeleft);
		var keyStrafeRight = IsPressed(config.key_straferight);
		var keyTurnLeft = IsPressed(config.key_turnleft);
		var keyTurnRight = IsPressed(config.key_turnright);
		var keyFire = IsPressed(config.key_fire);
		var keyUse = IsPressed(config.key_use);
		var keyRun = IsPressed(config.key_run);
		var keyStrafe = IsPressed(config.key_strafe);

		for (var i = 0; i < weaponKeys.Length; i++)
		{
			weaponKeys[i] = down[(int)DoomKey.Num1 + i];
		}

		cmd.Clear();

		var strafe = keyStrafe;
		var speed = keyRun ? 1 : 0;
		var forward = 0;
		var side = 0;

		if (config.game_alwaysrun)
		{
			speed = 1 - speed;
		}

		if (keyTurnLeft || keyTurnRight)
		{
			turnHeld++;
		}
		else
		{
			turnHeld = 0;
		}

		int turnSpeed;
		if (turnHeld < PlayerBehavior.SlowTurnTics)
		{
			turnSpeed = 2;
		}
		else
		{
			turnSpeed = speed;
		}

		if (strafe)
		{
			if (keyTurnRight)
			{
				side += PlayerBehavior.SideMove[speed];
			}
			if (keyTurnLeft)
			{
				side -= PlayerBehavior.SideMove[speed];
			}
		}
		else
		{
			if (keyTurnRight)
			{
				cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[turnSpeed];
			}
			if (keyTurnLeft)
			{
				cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[turnSpeed];
			}
		}

		if (keyForward)
		{
			forward += PlayerBehavior.ForwardMove[speed];
		}
		if (keyBackward)
		{
			forward -= PlayerBehavior.ForwardMove[speed];
		}

		if (keyStrafeLeft)
		{
			side -= PlayerBehavior.SideMove[speed];
		}
		if (keyStrafeRight)
		{
			side += PlayerBehavior.SideMove[speed];
		}

		if (keyFire)
		{
			cmd.Buttons |= TicCmdButtons.Attack;
		}

		if (keyUse)
		{
			cmd.Buttons |= TicCmdButtons.Use;
		}

		for (var i = 0; i < weaponKeys.Length; i++)
		{
			if (weaponKeys[i])
			{
				cmd.Buttons |= TicCmdButtons.Change;
				cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift);
				break;
			}
		}

		// Mouse: turning only, whatever else is held. (The original also moved forward/back with the mouse's up/down and strafed with it while
		// the strafe key was held; both are left out on purpose.)
		// At a low sensitivity one pixel is less than one turning step, so what is left over after rounding is kept for the next tic instead of being lost;
		// otherwise slow movements would do nothing at all.
		var wanted = 0.5 * config.mouse_sensitivity * mouseTurn + mouseRemainder;
		var mouseSteps = (int)Math.Round(wanted);
		mouseRemainder = wanted - mouseSteps;
		mouseTurn = 0;
		cmd.AngleTurn -= (short)Math.Clamp(mouseSteps * 0x8, -short.MaxValue / 2, short.MaxValue / 2);

		forward = Math.Clamp(forward, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
		side = Math.Clamp(side, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
		cmd.ForwardMove += (sbyte)forward;
		cmd.SideMove += (sbyte)side;
	}

	public void Reset()
	{
		turnHeld = 0;
		mouseTurn = 0;
		mouseRemainder = 0;
	}

	// The cursor is never captured: it stays free. The popup only reports its movement while it is over the picture.
	public void GrabMouse()
	{
	}

	public void ReleaseMouse()
	{
	}

	public int MaxMouseSensitivity => 15;

	public int MouseSensitivity
	{
		get => config.mouse_sensitivity;
		set => config.mouse_sensitivity = value;
	}
}
