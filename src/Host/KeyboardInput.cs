#nullable enable
using System;
using System.Collections.Generic;
using ManagedDoom;
using ManagedDoom.UserInput;

namespace DoomedSpire.Host;

/// <summary>
/// The player's keyboard as the engine sees it. The popup tells it which keys are down; every game tic the engine asks it for the player's command
/// (move, turn, fire, use, weapon). A port of the keyboard half of the original frontend's SilkUserInput.BuildTicCmd, with the same default keys
/// (Up/W forward, Down/S back, A/D strafe, Left/Right turn, Ctrl fire, Space use, Shift run, Alt strafe-with-turn keys, 1-7 weapons).
/// The mouse half comes in a later piece; for now the mouse does nothing. Godot-free (the tests use it).
/// </summary>
internal sealed class KeyboardInput : IUserInput
{
	private readonly Config config;
	private readonly bool[] down = new bool[(int)DoomKey.Count];
	private readonly bool[] weaponKeys = new bool[7];
	private int turnHeld;

	public KeyboardInput(Config config)
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

		forward = Math.Clamp(forward, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
		side = Math.Clamp(side, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
		cmd.ForwardMove += (sbyte)forward;
		cmd.SideMove += (sbyte)side;
	}

	public void Reset()
	{
		turnHeld = 0;
	}

	// Mouse: a later piece.
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
