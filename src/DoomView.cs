using System;
using System.Diagnostics;
using System.IO;
using DoomedSpire.Host;
using Godot;
using ManagedDoom;

namespace DoomedSpire;

/// <summary>
/// The picture area inside the popup, and the running game behind it. Start() builds a game and begins playing it; Stop() throws it away.
/// The Godot side only: the engine work (the game itself, keyboard and mouse) is in src/Host, which the tests run without the game.
///
/// No custom Godot classes (a dll-only mod cannot register scripts), so everything is wired with signals:
///  - every frame comes from SceneTree.ProcessFrame, connected only while the game is running;
///  - keys and the mouse arrive through the GuiInput signal of the picture area, which holds keyboard focus while the game runs.
///
/// Keys: every key goes to DOOM except Esc, which leaves DOOM and drops back to the map screen (it leaves on release, so both halves of the key stay in DOOM). The backtick key is DOOM's own Esc (its menu). Nothing reaches the game: the picture area accepts
/// every key event (stopping the game's _UnhandledInput hotkeys), and KeyLock (src/KeyLock.cs) stops the handlers that read keys before the GUI.
/// Mouse: while DOOM runs the mouse is captured (Input.MouseMode Captured): it is locked to the game window, so it can never wander onto another
/// monitor, its cursor is hidden, and it gives unlimited relative movement, so turning never stops at a screen edge. Any sideways movement turns
/// the player (read from the window's own input signal, not from a control, so it counts wherever the "cursor" is). Left click fires and right click
/// uses from anywhere: the popup covers the whole screen on its own canvas layer, so nothing else in the game can be clicked (DoomPopup).
/// The mouse never walks the player (WASD does). The previous mouse mode is put back whenever DOOM ends, whichever way it ends.
/// </summary>
internal sealed class DoomView
{
	/// <summary>DOOM's clock: 35 game tics a second.</summary>
	private const double TicSeconds = 1.0 / 35.0;
	private const int MaxFailuresBeforeStopping = 5;

	private readonly Control screen;
	private readonly Godot.Button closeButton;
	private readonly TextureRect picture;
	private readonly Label message;

	private DoomSession? session;
	private byte[]? rgba;
	private Image? image;
	private ImageTexture? texture;

	private SceneTree? tree;
	private Action? frameHandler;
	private long lastTimestamp;
	private double accumulator;
	private bool windowWasUnfocused;
	private Window? inputWindow;
	private Input.MouseModeEnum mouseModeBefore;
	private bool mouseCaptured;
	private bool escapeWasDown;
	private int failures;

	/// <summary>Called when DOOM itself wants to end (the player quit from DOOM's menu). The popup sets this to close itself.</summary>
	public Action? RequestClose { get; set; }

	/// <summary>
	/// Adds the picture and the message label to <paramref name="screen"/>, the 4:3 black area of the popup. <paramref name="mouseArea"/> is the full-screen
	/// control that receives every mouse click; <paramref name="closeButton"/> is hidden while a game runs, so a click meant for DOOM cannot close the popup.
	/// </summary>
	public DoomView(Control screen, Control mouseArea, Godot.Button closeButton)
	{
		this.screen = screen;
		this.closeButton = closeButton;

		// DOOM's pixels are not square: its 640x400 picture is meant to be shown as 4:3, so the picture is stretched to fill the whole area.
		picture = new TextureRect
		{
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		picture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		screen.AddChild(picture);

		message = new Label
		{
			Text = "DOOM goes here",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		message.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 24);
		screen.AddChild(message);

		// The picture area holds keyboard focus while the game runs, and receives the keys. The mouse clicks arrive at the full-screen mouse area.
		screen.FocusMode = Control.FocusModeEnum.All;
		screen.GuiInput += OnGuiInput;
		mouseArea.GuiInput += OnGuiInput;
	}

	/// <summary>Called when the popup opens: gets the game data ready (the embedded Freedoom, written to disk the first time), starts a fresh game and begins playing it.</summary>
	public void Start()
	{
		if (session != null)
		{
			return;
		}

		try
		{
			var timer = Stopwatch.StartNew();
			var wad = FreedoomData.EnsureOnDisk();
			var dataReady = timer.ElapsedMilliseconds;
			session = DoomSession.Create(wad);
			rgba = new byte[session.Width * session.Height * 4];
			image = Image.CreateFromData(session.Width, session.Height, false, Image.Format.Rgba8, rgba);
			texture = ImageTexture.CreateFromImage(image);
			picture.Texture = texture;
			message.Visible = false;

			failures = 0;
			accumulator = 0;
			windowWasUnfocused = false;
			escapeWasDown = false;
			lastTimestamp = Stopwatch.GetTimestamp();
			tree = screen.GetTree();
			frameHandler = OnFrame;
			tree.ProcessFrame += frameHandler;
			inputWindow = tree.Root;
			inputWindow.WindowInput += OnWindowInput;
			CaptureMouse();
			screen.GrabFocus();
			KeyLock.Active = true;
			closeButton.Visible = false;
			ModLog.Info("game", $"started from {Path.GetFileName(wad)} ({session.Width}x{session.Height}) in {timer.ElapsedMilliseconds} ms (game data ready after {dataReady} ms)");
		}
		catch (Exception e)
		{
			ModLog.Fail("game", "starting DOOM", e);
			Stop();
			Say($"Could not start DOOM.\n\n{e.GetType().Name}: {ModLog.Clean(e.Message)}");
		}
	}

	/// <summary>Called when the popup closes (or the map does): stops the frames, lets go of every key and the keyboard focus, and throws the game away.</summary>
	public void Stop()
	{
		try
		{
			if (tree != null && frameHandler != null && GodotObject.IsInstanceValid(tree))
			{
				tree.ProcessFrame -= frameHandler;
			}
			frameHandler = null;
			tree = null;

			if (inputWindow != null && GodotObject.IsInstanceValid(inputWindow))
			{
				inputWindow.WindowInput -= OnWindowInput;
			}
			inputWindow = null;

			session?.ReleaseAllInput();
			session?.Dispose();
		}
		catch (Exception e)
		{
			ModLog.Fail("game", "stopping DOOM", e);
		}
		finally
		{
			ReleaseMouse();
			KeyLock.Active = false;
			if (GodotObject.IsInstanceValid(closeButton))
			{
				closeButton.Visible = true;
			}
			session = null;
			rgba = null;
			image = null;
			texture = null;
			picture.Texture = null;
			if (GodotObject.IsInstanceValid(screen) && screen.HasFocus())
			{
				screen.ReleaseFocus();
			}
		}
	}

	/// <summary>Every rendered frame while the game runs: run the game clock, then draw.</summary>
	private void OnFrame()
	{
		var current = session;
		var pixels = rgba;
		var target = image;
		var tex = texture;
		if (current == null || pixels == null || target == null || tex == null)
		{
			return;
		}

		try
		{
			// While the game window is not in front (alt-tab), the game stands still and no key stays held.
			if (!DisplayServer.WindowIsFocused())
			{
				if (!windowWasUnfocused)
				{
					current.ReleaseAllInput();
					windowWasUnfocused = true;
				}
				lastTimestamp = Stopwatch.GetTimestamp();
				if (Input.MouseMode == Input.MouseModeEnum.Captured)
				{
					Input.MouseMode = Input.MouseModeEnum.Visible; // do not keep the mouse while the window is not in front
				}
				return;
			}
			windowWasUnfocused = false;

			// Keep the mouse captured. The game sets the mouse mode itself whenever it shows or hides its cursor (NCursorManager.RefreshCursorShown),
			// which would undo it, so it is put back every frame.
			if (Input.MouseMode != Input.MouseModeEnum.Captured)
			{
				Input.MouseMode = Input.MouseModeEnum.Captured;
			}

			// Esc leaves DOOM, on release. Read from the keyboard state itself every frame rather than from key events, so the way out never depends on
			// keyboard focus or event delivery: the Close button is hidden while a game runs and every other key is locked, so Esc must always work.
			var escapeDown = Input.IsKeyPressed(Key.Escape);
			if (escapeWasDown && !escapeDown)
			{
				escapeWasDown = false;
				RequestClose?.Invoke();
				return;
			}
			escapeWasDown = escapeDown;

			var now = Stopwatch.GetTimestamp();
			var elapsed = (double)(now - lastTimestamp) / Stopwatch.Frequency;
			lastTimestamp = now;
			accumulator += Math.Min(elapsed, 0.25); // after a long hitch, do not try to catch up on everything

			var steps = 0;
			while (accumulator >= TicSeconds)
			{
				accumulator -= TicSeconds;
				if (current.Tick() == UpdateResult.Completed)
				{
					ModLog.Info("game", "the player quit from DOOM's menu");
					RequestClose?.Invoke();
					return;
				}
				if (++steps >= 8)
				{
					accumulator = 0; // far behind: drop the backlog instead of spiralling
					break;
				}
			}

			if (!screen.HasFocus())
			{
				screen.GrabFocus(); // something else took the keyboard (a click elsewhere): take it back, or the keys would reach the game
			}

			current.RenderTo(pixels, accumulator / TicSeconds);
			target.SetData(current.Width, current.Height, false, Image.Format.Rgba8, pixels);
			tex.Update(target);
		}
		catch (Exception e)
		{
			ModLog.Fail("game", "one frame", e);
			if (++failures >= MaxFailuresBeforeStopping)
			{
				ModLog.Warn("game", $"{failures} frames failed; stopping DOOM so it cannot slow the game down");
				Stop();
				Say("DOOM stopped after repeated errors. Close this and open it again to retry; see the log for details.");
			}
		}
	}

	/// <summary>
	/// Every input event that reaches the picture area. Each one is accepted, so nothing else in the game reacts to it.
	/// Keys go to DOOM, except Esc. The mouse buttons shoot and use, from anywhere on the screen (turning is read every frame, see OnFrame).
	/// </summary>
	private void OnGuiInput(InputEvent inputEvent)
	{
		try
		{
			var current = session;
			if (current == null)
			{
				return;
			}

			switch (inputEvent)
			{
				case InputEventKey key:
					screen.AcceptEvent();
					if (key.Echo)
					{
						return;
					}
					if (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape)
					{
						return; // swallowed here so nothing else reacts to it; leaving is done in OnFrame
					}
					var doomKey = KeyMap.ToDoom(key);
					if (doomKey != DoomKey.Unknown)
					{
						current.PostKey(doomKey, key.Pressed);
					}
					break;

				case InputEventMouseButton button:
					screen.AcceptEvent();
					if (button.ButtonIndex == MouseButton.Left)
					{
						current.PostMouseButton(DoomMouseButton.Mouse1, button.Pressed);
					}
					else if (button.ButtonIndex == MouseButton.Right)
					{
						current.PostMouseButton(DoomMouseButton.Mouse2, button.Pressed);
					}
					break;
			}
		}
		catch (Exception e)
		{
			ModLog.Fail("input", "handling an input event", e);
		}
	}

	/// <summary>Every event the game window receives, before anything else sees it. Only the mouse's sideways movement is used here: it turns the player.</summary>
	private void OnWindowInput(InputEvent inputEvent)
	{
		try
		{
			if (inputEvent is InputEventMouseMotion motion)
			{
				session?.AddMouseTurn(motion.Relative.X);
			}
		}
		catch (Exception e)
		{
			ModLog.Fail("input", "reading mouse movement", e);
		}
	}

	private void CaptureMouse()
	{
		if (!mouseCaptured)
		{
			mouseModeBefore = Input.MouseMode;
			mouseCaptured = true;
		}
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	/// <summary>Puts the mouse back the way it was, if it is still ours. Safe to call any number of times.</summary>
	private void ReleaseMouse()
	{
		try
		{
			if (mouseCaptured)
			{
				mouseCaptured = false;
				if (Input.MouseMode == Input.MouseModeEnum.Captured || Input.MouseMode == Input.MouseModeEnum.Visible)
				{
					Input.MouseMode = mouseModeBefore == Input.MouseModeEnum.Captured ? Input.MouseModeEnum.Visible : mouseModeBefore;
				}
			}
		}
		catch (Exception e)
		{
			ModLog.Fail("input", "giving the mouse back", e);
		}
	}

	private void Say(string text)
	{
		message.Text = text;
		message.Visible = true;
	}
}
