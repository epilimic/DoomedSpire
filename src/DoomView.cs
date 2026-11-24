using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DoomedSpire.Host;
using Godot;
using ManagedDoom;

namespace DoomedSpire;

/// <summary>
/// The picture area inside the popup, and the game behind it. Start() starts a game or carries on the paused one; Suspend() pauses it in place when the
/// popup closes (the game stays in memory); Shutdown() throws it away when the run ends.
/// The Godot side only: the engine work (the game itself, keyboard, mouse, sound and music) is in src/Host, which the tests run without the game.
///
/// No custom Godot classes (a dll-only mod cannot register scripts), so everything is wired with signals:
///  - every frame comes from SceneTree.ProcessFrame, connected only while the game is running;
///  - keys and the mouse arrive through the GuiInput signal of the picture area, which holds keyboard focus while the game runs.
///
/// Keys: every key goes to DOOM, Esc included (it opens and closes DOOM's own menu, whose Quit Game ends the game and leaves). The one exception is the backtick key,
/// which is not DOOM's: it toggles between DOOM and the map screen (here, DOOM to the map; on the map screen it is ToggleHotkey, through KeyLock), and the
/// game stays in memory exactly where it was. Nothing reaches the game: the picture area accepts
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

	/// <summary>
	/// A canvas layer at or above this number, visible and with something visible on it, is a tool open on top of DOOM (VfxSpawner's menu is on layer 128). DOOM
	/// steps aside for it. Below this: the game's own layers (everything on 0, the dev console on 10, which stays visible even when its panel is closed) and
	/// DOOM's own (8).
	/// </summary>
	private const int OverlayLayerThreshold = 100;

	/// <summary>The width of the game picker's buttons, on the 1920x1080 canvas (the picture area is 1024 wide).</summary>
	private const float PickerWidth = 720f;

	private readonly Control screen;
	private readonly TextureRect picture;
	private readonly Label message;
	private readonly Control pickerRoot;
	private readonly Label pickerTitle;
	private readonly VBoxContainer choiceList;
	private readonly VBoxContainer titleList;
	private readonly VBoxContainer footerList;
	private IReadOnlyList<(string Text, WadFile? Wad)> pickerChoices = Array.Empty<(string, WadFile?)>();
	private IReadOnlyList<WadFile> pickerSaved = Array.Empty<WadFile>();
	private int pickerOffset;
	private int pickerRows;
	private Godot.Button? upArrow;
	private Godot.Button? downArrow;
	private readonly List<TextureRect> skullRects = new();
	private int skullFrame;

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
	private bool toggleKeyWasDown;
	private bool yielding;
	private int overlayCheckCountdown;
	private bool overlayOpen;
	private readonly CanvasLayer? ourLayer;
	private bool active;
	private bool endGame;
	private AudioOutput? audioOutput;
	private int failures;

	/// <summary>
	/// True while no game is running (the game picker, a message, or a tool open on top of DOOM): then a click on the map screen's DOOM button may close the popup. While a
	/// game runs every click is DOOM's (shooting and using), so the button is not taken then and the backtick key is the way out.
	/// </summary>
	public bool CanCloseWithButton { get; private set; } = true;

	/// <summary>Called when DOOM itself wants to end (the player quit from DOOM's menu). The popup sets this to close itself.</summary>
	public Action? RequestClose { get; set; }

	/// <summary>
	/// Adds the picture and the message label to <paramref name="screen"/>, the 4:3 black area of the popup. <paramref name="mouseArea"/> is the full-screen
	/// control that receives every mouse click.
	/// </summary>
	public DoomView(Control screen, Control mouseArea)
	{
		this.screen = screen;
		ourLayer = mouseArea.GetParent() as CanvasLayer;

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
			Visible = false,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		message.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 24);
		screen.AddChild(message);

		// The game picker: shown instead of starting a game right away when the player has saved their own DOOM games (see Start). Hidden otherwise.
		pickerRoot = new CenterContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
		pickerRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		screen.AddChild(pickerRoot);
		var pickerBox = new VBoxContainer { CustomMinimumSize = new Vector2(PickerWidth, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
		pickerBox.AddThemeConstantOverride("separation", 10);
		pickerRoot.AddChild(pickerBox);
		pickerTitle = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(PickerWidth, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		pickerBox.AddChild(pickerTitle);
		titleList = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		pickerBox.AddChild(titleList);
		choiceList = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		choiceList.AddThemeConstantOverride("separation", 8);
		pickerBox.AddChild(choiceList);
		footerList = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		pickerBox.AddChild(footerList);

		// The skull cursor of DOOM's menus flips between its two pictures about four times a second (the engine's 8 tics at 35 a second).
		var skullTimer = new Godot.Timer { WaitTime = 8.0 / 35.0, OneShot = false, Autostart = true };
		skullTimer.Timeout += FlipSkulls;
		pickerRoot.AddChild(skullTimer);

		// The picture area holds keyboard focus while the game runs, and receives the keys. The mouse clicks arrive at the full-screen mouse area.
		screen.FocusMode = Control.FocusModeEnum.All;
		screen.GuiInput += OnGuiInput;
		mouseArea.GuiInput += OnGuiInput;
	}

	/// <summary>
	/// Called when the popup opens. If a game is paused, it carries on exactly where it was left. Otherwise it starts a new game: straight away with the
	/// built-in shareware DOOM if the player has saved no games of their own, or after the player picks from a short list (the built-in one and each game saved
	/// in the mod's wads folder).
	/// </summary>
	public void Start()
	{
		try
		{
			if (starting)
			{
				// a new game is still being got ready (the enemies are drawn first): carry on with it when it is, instead of starting a second one
				startCancelled = false;
				return;
			}
			if (session != null)
			{
				HidePicker();
				message.Visible = false;
				Activate(session);
				ModLog.Info("game", "carrying on the paused game");
				return;
			}

			var local = FindSavedGames();
			if (local.Count == 0)
			{
				BeginGame(null, local);
			}
			else
			{
				ShowPicker(local, null);
			}
		}
		catch (Exception e)
		{
			ModLog.Fail("game", "opening DOOM", e);
			Shutdown();
			Say($"Could not start DOOM.\n\n{e.GetType().Name}: {ModLog.Clean(e.Message)}");
		}
	}

	/// <summary>True while a new game is being got ready: the enemies are drawn over a number of frames, so starting is not instant.</summary>
	private bool starting;
	/// <summary>The popup was closed while the new game was being got ready: the game is made but not started until the popup is opened again.</summary>
	private bool startCancelled;
	/// <summary>Counts the times everything was thrown away (<see cref="Shutdown"/>), so a game that was being got ready then is dropped.</summary>
	private int shutdowns;

	/// <summary>
	/// Starts a new game from <paramref name="wad"/>, or from the built-in game when that is null. The enemies are drawn first (shareware game only; this takes a
	/// moment over several frames), then the game starts. If it fails, says why, and offers the list again if there is one.
	/// </summary>
	private async void BeginGame(WadFile? wad, IReadOnlyList<WadFile> saved)
	{
		if (starting)
		{
			return;
		}
		HidePicker();
		message.Visible = false; // an earlier error is gone; the screen stays black until the game is ready
		var label = wad?.Name ?? BuiltInWad.Label;
		starting = true;
		startCancelled = false;
		var shutdownsAtStart = shutdowns;
		try
		{
			var timer = Stopwatch.StartNew();
			var path = wad?.Path ?? BuiltInWad.EnsureOnDisk();
			var dataReady = timer.ElapsedMilliseconds;
			var dataFolder = Path.Combine(BuiltInWad.DataFolder, "data");
			// The character's face for the status bar, as an add-on WAD (null keeps Doomguy). Made here, each new game, from the current tuning.
			var faces = PlayerFace.TryBuild(path, dataFolder);
			// Slay the Spire enemies for DOOM's monsters, as another add-on WAD (null keeps DOOM's own). Only for the original DOOM 1 games.
			var enemies = await EnemySprites.TryBuildAsync(screen, path, dataFolder);
			if (shutdowns != shutdownsAtStart || !GodotObject.IsInstanceValid(screen))
			{
				return; // everything was thrown away while this was going on
			}
			var addOns = new List<string>();
			if (faces != null) addOns.Add(faces);
			if (enemies != null) addOns.Add(enemies);
			var keys = KeyTheme.TryBuild(path, dataFolder); // the keycards as the Fool's melody cards (original DOOM 1 games only)
			if (keys != null) addOns.Add(keys);
			var audio = new AudioOptions { OpenSoundFont = SoundFontData.Open, Report = ReportAudioProblem };
			DoomSession game;
			try
			{
				game = DoomSession.Create(path, audio, addOns.Count == 0 ? null : addOns);
			}
			catch (Exception e) when (addOns.Count > 0)
			{
				// the character's face or the enemy pictures broke the game: start without them rather than not at all
				ModLog.Fail("game", "starting with the face and enemy pictures (starting without them instead)", e);
				game = DoomSession.Create(path, audio, null);
			}
			session = game;
			rgba = new byte[game.Width * game.Height * 4];
			image = Image.CreateFromData(game.Width, game.Height, false, Image.Format.Rgba8, rgba);
			texture = ImageTexture.CreateFromImage(image);
			picture.Texture = texture;
			message.Visible = false;
			if (!startCancelled)
			{
				Activate(game);
			}
			ModLog.Info("game", $"new game: {label} ({game.Width}x{game.Height}) in {timer.ElapsedMilliseconds} ms " +
				$"(game data ready after {dataReady} ms; {game.LoadedSoundCount} sounds, music {(game.HasMusic ? "on" : "off")}{(startCancelled ? "; the popup was closed meanwhile, so it waits paused" : "")})");
		}
		catch (Exception e)
		{
			ModLog.Fail("game", $"starting {label}", e);
			Shutdown();
			var problem = $"Could not start {label}.\n{e.GetType().Name}: {ModLog.Clean(e.Message)}";
			if (saved.Count > 0)
			{
				ShowPicker(saved, problem);
			}
			else
			{
				Say(problem);
			}
		}
		finally
		{
			starting = false;
		}
	}

	/// <summary>The games the player saved in the mod's wads folder (made if it is not there yet, so the player can see where it is).</summary>
	private static IReadOnlyList<WadFile> FindSavedGames()
	{
		var folder = SavedGamesFolder;
		ModLog.Try("game", "creating the wads folder", () => DirAccess.MakeDirRecursiveAbsolute(folder));
		var games = WadLibrary.FindGames(folder, text => ModLog.OnceInfo("game", text, text));
		ModLog.OnceInfo("game", "wads-folder", $"DOOM games saved in {folder} are offered when DOOM opens; found {games.Count} when this was first checked");
		return games;
	}

	private static string SavedGamesFolder => Path.Combine(BuiltInWad.DataFolder, "wads");

	private void ShowPicker(IReadOnlyList<WadFile> saved, string? problem)
	{
		ClearPicker();
		var font = DoomText.Font;
		var choices = WadLibrary.PickerChoices(saved, BuiltInWad.Label);
		pickerChoices = choices;
		pickerSaved = saved;
		pickerOffset = 0;

		if (font != null && ModLog.Try("game", "making the game picker in DOOM's font", () => BuildDoomPicker(font, problem)))
		{
			pickerTitle.Visible = false;
		}
		else
		{
			// the plain game text: DOOM's font could not be made
			ClearPicker();
			foreach (var (text, wad) in choices)
			{
				AddChoice(text, wad, saved);
			}
			pickerTitle.Text = problem == null ? "Choose a game" : problem + "\n\nChoose a game";
			pickerTitle.Visible = true;
		}
		message.Visible = false;
		picture.Texture = null;
		pickerRoot.Visible = true;
	}

	private void ClearPicker()
	{
		foreach (var list in new[] { choiceList, titleList, footerList })
		{
			foreach (var child in list.GetChildren())
			{
				list.RemoveChild(child);
				child.QueueFree();
			}
		}
		skullRects.Clear();
		upArrow = null;
		downArrow = null;
	}

	/// <summary>The cursor's width and a gap, before a game's name.</summary>
	private const float SkullSlot = 84f;
	private const float RowHeight = 68f;
	private const float RowGap = 8f;
	private const float ArrowSlot = 40f;

	/// <summary>
	/// The picker the way DOOM draws its own menus: the title in DOOM's font twice as big, each game a line of its font with the skull cursor coming up beside the
	/// one the mouse is over. A list longer than fits shows a window of it: the mouse wheel (or the arrows above and below, which show only when there is more that
	/// way) moves it a game at a time. Throws if anything fails, so ShowPicker falls back to the plain controls.
	/// </summary>
	private void BuildDoomPicker(DoomFont font, string? problem)
	{
		var used = 80f; // the empty margin round the list
		if (problem != null)
		{
			var text = DoomText.Make(font, problem, PickerWidth, centred: true);
			titleList.AddChild(text);
			titleList.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20), MouseFilter = Control.MouseFilterEnum.Ignore });
			used += text.CustomMinimumSize.Y + 20;
		}
		var title = DoomText.Make(font, "Choose a game", PickerWidth, centred: true, zoom: 2);
		titleList.AddChild(title);
		titleList.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24), MouseFilter = Control.MouseFilterEnum.Ignore });
		used += title.CustomMinimumSize.Y + 24;

		var height = screen.Size.Y > 100 ? screen.Size.Y : 768f;
		var fits = Math.Max(3, (int)((height - used - 2 * ArrowSlot) / (RowHeight + RowGap)));
		pickerRows = Math.Min(pickerChoices.Count, fits);
		if (pickerChoices.Count > pickerRows)
		{
			upArrow = MakeArrow(font, up: true);
			downArrow = MakeArrow(font, up: false);
			titleList.AddChild(upArrow);
			footerList.AddChild(downArrow);
		}
		RebuildRows(font);
	}

	/// <summary>A flat button holding DOOM's arrow, which scrolls the list a game that way; it is shown (and takes the mouse) only while there is more in that direction.</summary>
	private Godot.Button MakeArrow(DoomFont font, bool up)
	{
		var button = new Godot.Button { CustomMinimumSize = new Vector2(PickerWidth, ArrowSlot), FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Pass };
		foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
		{
			button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
		}
		var arrow = font.Arrow(up);
		if (arrow != null)
		{
			var rect = new TextureRect
			{
				Texture = DoomText.Texture(arrow),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.Scale,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				CustomMinimumSize = new Vector2(arrow.Width * DoomText.PixelWidth * 2, arrow.Height * DoomText.PixelHeight * 2),
			};
			rect.Size = rect.CustomMinimumSize;
			rect.Position = new Vector2((PickerWidth - rect.Size.X) / 2, (ArrowSlot - rect.Size.Y) / 2);
			button.AddChild(rect);
		}
		button.Pressed += () => ModLog.Try("game", "scrolling the game list", () => ScrollPicker(up ? -1 : 1));
		return button;
	}

	/// <summary>Moves the window on the list by <paramref name="by"/> games, as far as the ends of the list allow.</summary>
	private void ScrollPicker(int by)
	{
		var font = DoomText.Font;
		if (font == null || pickerChoices.Count <= pickerRows)
		{
			return;
		}
		var offset = Math.Clamp(pickerOffset + by, 0, pickerChoices.Count - pickerRows);
		if (offset != pickerOffset)
		{
			pickerOffset = offset;
			RebuildRows(font);
		}
	}

	/// <summary>Puts the games in the window (from <see cref="pickerOffset"/>) in the list and shows the arrows that have more to scroll to.</summary>
	private void RebuildRows(DoomFont font)
	{
		foreach (var child in choiceList.GetChildren())
		{
			choiceList.RemoveChild(child);
			child.QueueFree();
		}
		skullRects.Clear();
		for (var i = pickerOffset; i < pickerOffset + pickerRows && i < pickerChoices.Count; i++)
		{
			choiceList.AddChild(MakeChoiceRow(font, pickerChoices[i].Text, pickerChoices[i].Wad));
		}
		SetArrow(upArrow, pickerOffset > 0);
		SetArrow(downArrow, pickerOffset + pickerRows < pickerChoices.Count);
	}

	private static void SetArrow(Godot.Button? button, bool shown)
	{
		if (button == null)
		{
			return;
		}
		button.MouseFilter = shown ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
		foreach (var child in button.GetChildren())
		{
			if (child is CanvasItem item)
			{
				item.Visible = shown;
			}
		}
	}

	/// <summary>One game: a line of DOOM's font (cut with ... if it is too long) with the skull cursor beside it while the mouse is over it.</summary>
	private Godot.Button MakeChoiceRow(DoomFont font, string text, WadFile? wad)
	{
		var label = DoomText.Make(font, font.Truncate(text, (int)((PickerWidth - SkullSlot) / DoomText.PixelWidth)), PickerWidth - SkullSlot);
		label.Position = new Vector2(SkullSlot, (RowHeight - label.CustomMinimumSize.Y) / 2);

		// Pass, not Stop: the mouse wheel over a game must reach the popup's root, which scrolls the list
		var button = new Godot.Button { CustomMinimumSize = new Vector2(PickerWidth, RowHeight), FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Pass };
		foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
		{
			button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
		}
		button.AddChild(label);
		var skull = DoomText.MakeSkull(font);
		if (skull != null)
		{
			skull.Position = new Vector2(0, (RowHeight - skull.Size.Y) / 2);
			skull.Visible = false;
			button.AddChild(skull);
			skullRects.Add(skull);
			button.MouseEntered += () => { if (GodotObject.IsInstanceValid(skull)) skull.Visible = true; };
			button.MouseExited += () => { if (GodotObject.IsInstanceValid(skull)) skull.Visible = false; };
		}
		var saved = pickerSaved;
		button.Pressed += () => ModLog.Try("game", "starting the chosen game", () => BeginGame(wad, saved));
		return button;
	}

	/// <summary>Timer: the skull cursors take their other picture.</summary>
	private void FlipSkulls()
	{
		try
		{
			skullFrame ^= 1;
			var font = DoomText.Font;
			if (font == null || font.SkullCount == 0)
			{
				return;
			}
			var texture = DoomText.SkullTexture(font, skullFrame);
			foreach (var skull in skullRects)
			{
				if (GodotObject.IsInstanceValid(skull) && skull.Visible)
				{
					skull.Texture = texture;
				}
			}
		}
		catch (Exception e)
		{
			ModLog.Fail("game", "flipping the skull cursor", e);
		}
	}

	private void AddChoice(string text, WadFile? wad, IReadOnlyList<WadFile> saved)
	{
		var button = new Godot.Button { Text = text, CustomMinimumSize = new Vector2(PickerWidth, 56), FocusMode = Control.FocusModeEnum.None };
		button.Pressed += () => ModLog.Try("game", "starting the chosen game", () => BeginGame(wad, saved));
		choiceList.AddChild(button);
	}

	private void HidePicker()
	{
		if (GodotObject.IsInstanceValid(pickerRoot))
		{
			pickerRoot.Visible = false;
		}
	}

	/// <summary>
	/// Called when the popup closes (the backtick key, the map closing, or DOOM's own quit). Everything is switched off at once: the frames, the audio, the mouse capture,
	/// the key lock. The game itself stays in memory, paused exactly where it is, so opening the popup again carries on. Only if the player quit from
	/// DOOM's menu is the game thrown away.
	/// </summary>
	public void Suspend()
	{
		if (starting)
		{
			startCancelled = true;
		}
		Deactivate();
		HidePicker();
		if (endGame)
		{
			endGame = false;
			DisposeGame();
		}
	}

	/// <summary>The end of everything: the run is over (the map screen is gone), or something failed. Switches off and throws the game away.</summary>
	public void Shutdown()
	{
		shutdowns++;
		Deactivate();
		HidePicker();
		DisposeGame();
	}

	/// <summary>Connects the frame loop, the mouse, the keys and the audio to a game, and starts the clock from now (so time spent closed is not caught up).</summary>
	private void Activate(DoomSession game)
	{
		active = true;
		failures = 0;
		accumulator = 0;
		windowWasUnfocused = false;
		toggleKeyWasDown = ToggleKeyDown(); // a key still held from opening DOOM must not close it again
		yielding = false;
		overlayOpen = false;
		overlayCheckCountdown = 0;
		lastTimestamp = Stopwatch.GetTimestamp();

		var (soundGain, musicGain) = GameVolume.Read();
		game.SetAudioGains(soundGain, musicGain);

		tree = screen.GetTree();
		frameHandler = OnFrame;
		tree.ProcessFrame += frameHandler;
		inputWindow = tree.Root;
		inputWindow.WindowInput += OnWindowInput;
		CaptureMouse();
		screen.GrabFocus();
		KeyLock.Active = true;
		CanCloseWithButton = false;

		try
		{
			audioOutput = new AudioOutput(screen);
			audioOutput.Start();
		}
		catch (Exception e)
		{
			// No sound is better than no game: carry on silently.
			ModLog.Fail("audio", "starting the audio output", e);
			audioOutput?.Stop();
			audioOutput = null;
		}
	}

	/// <summary>Everything Activate did, undone. Safe to call when not active and any number of times. Does not touch the game itself.</summary>
	private void Deactivate()
	{
		if (!active)
		{
			return;
		}
		active = false;
		yielding = false;

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
			audioOutput?.Stop();
		}
		catch (Exception e)
		{
			ModLog.Fail("game", "pausing DOOM", e);
		}
		finally
		{
			audioOutput = null;
			ReleaseMouse();
			KeyLock.Active = false;
			CanCloseWithButton = true;
			if (GodotObject.IsInstanceValid(screen) && screen.HasFocus())
			{
				screen.ReleaseFocus();
			}
		}
	}

	private void DisposeGame()
	{
		try
		{
			session?.Dispose();
		}
		catch (Exception e)
		{
			ModLog.Fail("game", "closing the game", e);
		}
		session = null;
		rgba = null;
		image = null;
		texture = null;
		if (GodotObject.IsInstanceValid(picture))
		{
			picture.Texture = null;
		}
	}

	private static void ReportAudioProblem(string text)
	{
		ModLog.OnceInfo("audio", text, text);
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

			// A tool opened on top of DOOM (the VfxSpawner tuner, say): the mouse and the keyboard are the tool's until it closes, and the game waits.
			if (ToolIsOpenOnTop())
			{
				if (!yielding)
				{
					yielding = true;
					current.ReleaseAllInput();
					KeyLock.Active = false;
					CanCloseWithButton = true; // always a way out, whatever the detection says
					if (Input.MouseMode == Input.MouseModeEnum.Captured)
					{
						Input.MouseMode = mouseModeBefore == Input.MouseModeEnum.Captured ? Input.MouseModeEnum.Visible : mouseModeBefore;
					}
					if (screen.HasFocus())
					{
						screen.ReleaseFocus();
					}
					ModLog.Info("game", "a tool is open on top of DOOM; the game waits and the mouse and keys are the tool's until it closes");
				}
				lastTimestamp = Stopwatch.GetTimestamp();
				return;
			}
			if (yielding)
			{
				yielding = false;
				KeyLock.Active = true;
				CanCloseWithButton = false;
				toggleKeyWasDown = ToggleKeyDown();
				lastTimestamp = Stopwatch.GetTimestamp();
				ModLog.Info("game", "the tool closed; the game carries on");
			}

			// Keep the mouse captured. The game sets the mouse mode itself whenever it shows or hides its cursor (NCursorManager.RefreshCursorShown),
			// which would undo it, so it is put back every frame.
			if (Input.MouseMode != Input.MouseModeEnum.Captured)
			{
				Input.MouseMode = Input.MouseModeEnum.Captured;
			}

			// The backtick key leaves DOOM for the map screen, when it goes down. Read from the keyboard state itself every frame rather than from key events, so
			// the way out never depends on keyboard focus or event delivery: every other key is DOOM's.
			var toggleDown = ToggleKeyDown();
			if (toggleDown && !toggleKeyWasDown)
			{
				toggleKeyWasDown = true;
				RequestClose?.Invoke();
				return;
			}
			toggleKeyWasDown = toggleDown;

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
					endGame = true; // unlike the backtick key, quitting ends the game: the next time starts a new one
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

			audioOutput?.Pump(current);

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
				Shutdown();
				Say("DOOM stopped after repeated errors. Click the DOOM button to close this, then open it again to retry; see the log for details.");
			}
		}
	}

	/// <summary>
	/// Every input event that reaches the picture area. Each one is accepted, so nothing else in the game reacts to it.
	/// Keys go to DOOM (Esc too), except the backtick key. The mouse buttons shoot and use, from anywhere on the screen (turning is read every frame, see OnFrame).
	/// </summary>
	private void OnGuiInput(InputEvent inputEvent)
	{
		try
		{
			var current = session;
			if (current == null)
			{
				// the game picker: the mouse wheel scrolls a long list
				if (pickerRoot.Visible && inputEvent is InputEventMouseButton { Pressed: true } wheel)
				{
					if (wheel.ButtonIndex == MouseButton.WheelUp)
					{
						ScrollPicker(-1);
					}
					else if (wheel.ButtonIndex == MouseButton.WheelDown)
					{
						ScrollPicker(1);
					}
				}
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
			// Not while a tool is open on top (the mouse is the tool's, and the game waits: what was turned meanwhile would all come at once when it carries on).
			if (inputEvent is InputEventMouseMotion motion && active && !yielding)
			{
				session?.AddMouseTurn(motion.Relative.X);
			}
		}
		catch (Exception e)
		{
			ModLog.Fail("input", "reading mouse movement", e);
		}
	}

	/// <summary>
	/// Is some other canvas layer, above ours and at or above <see cref="OverlayLayerThreshold"/>, showing something? Looked at about ten times a second:
	/// listing the layers is not free, and a tool opening is not urgent.
	/// </summary>
	private bool ToolIsOpenOnTop()
	{
		if (overlayCheckCountdown-- > 0)
		{
			return overlayOpen;
		}
		overlayCheckCountdown = 6;

		var found = false;
		var root = tree?.Root;
		var own = ourLayer;
		if (root != null && own != null)
		{
			foreach (var child in root.GetChildren())
			{
				if (child is CanvasLayer other && other.Visible && other.Layer >= OverlayLayerThreshold && other.Layer > own.Layer
					&& other.GetChildren().Any(c => c is CanvasItem item && item.Visible))
				{
					found = true;
					break;
				}
			}
		}
		overlayOpen = found;
		return found;
	}

	/// <summary>Is the backtick key down, by its label or by its place on the keyboard (the key under Esc)?</summary>
	private static bool ToggleKeyDown() => Input.IsKeyPressed(Key.Quoteleft) || Input.IsPhysicalKeyPressed(Key.Quoteleft);

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
