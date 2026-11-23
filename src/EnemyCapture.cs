using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DoomedSpire.Host;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace DoomedSpire;

/// <summary>
/// Takes pictures of one of the game's enemies in the poses DOOM needs. The enemy is a Spine skeleton animation, so it is put in a small off-screen
/// viewport (a SubViewport with a transparent background, never shown), held at one moment of one of its animations, and the viewport is read back as a
/// picture. The pattern is the game's own: the creature scene from <c>MonsterModel.CreateVisuals</c>, animation steps as in
/// <c>CreatureAnimator</c> (set the animation, set the track time, update the state by 0, apply it to the skeleton), the viewport read as in
/// <c>NMapShareButton</c> (RenderTargetUpdateMode.Once, then RenderingServer.FramePostDraw, then GetTexture().GetImage()).
/// </summary>
internal static class EnemyCapture
{
	/// <summary>The viewport is this many pixels square, and the creature is scaled to fit it (it is scaled down again to DOOM's tiny size afterwards).</summary>
	private const int Size = 640;
	/// <summary>Where the creature's origin (the point it stands on) goes in the viewport: the middle of the width and near the bottom.</summary>
	private const float FeetX = Size / 2f;
	private const float FeetY = Size - 50f;
	/// <summary>Frames to wait for the creature's skeleton to be built (it is built in the background) before giving up.</summary>
	private const int SkeletonWaitFrames = 240;

	/// <summary>
	/// A picture for each request the creature could do (a request whose animations the creature does not have is left out). Null if the creature cannot be
	/// drawn at all. <paramref name="host"/> is any node in the scene tree; the viewport is added under it for a moment.
	/// </summary>
	public static async Task<Dictionary<char, CapturedFrame>?> CaptureAsync(Node host, MonsterModel monster, IReadOnlyList<FrameRequest> requests)
	{
		var name = monster.Id.Entry;
		SubViewport? viewport = null;
		try
		{
			var visuals = monster.CreateVisuals();
			viewport = new SubViewport
			{
				Size = new Vector2I(Size, Size),
				TransparentBg = true,
				Disable3D = true,
				RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
			};
			host.AddChild(viewport);
			viewport.AddChild(visuals); // its _Ready runs now
			visuals.Position = new Vector2(FeetX, FeetY);

			var sprite = visuals.SpineBody;
			if (sprite == null)
			{
				ModLog.Info("enemy", $"{name} has no Spine animation to take pictures of");
				return null;
			}
			for (var i = 0; i < SkeletonWaitFrames && !sprite.IsAnimationStateReady(); i++)
			{
				await NextFrame(host);
			}
			if (!sprite.IsAnimationStateReady())
			{
				ModLog.Info("enemy", $"{name}: its skeleton did not finish loading, so it cannot be drawn");
				return null;
			}

			// Scale the creature to fill the viewport: its Bounds rectangle (the game's own extent of the creature, in its local space) decides how much.
			var bounds = visuals.Bounds.GetGlobalRect();
			float left = bounds.Position.X - FeetX, top = bounds.Position.Y - FeetY, width = bounds.Size.X;
			var scale = 0.5f;
			if (top < -1f && width > 1f)
			{
				scale = Math.Min((FeetY - 20f) / -top, (Size / 2f - 20f) / Math.Max(1f, Math.Max(-left, left + width)));
			}
			visuals.Scale = new Vector2(Math.Clamp(scale, 0.05f, 4f), Math.Clamp(scale, 0.05f, 4f));

			var state = sprite.GetAnimationState();
			var skeleton = sprite.GetSkeleton();
			if (skeleton == null)
			{
				ModLog.Info("enemy", $"{name} has no skeleton to pose");
				return null;
			}
			state.SetTimeScale(0f); // the animation only moves when we say so

			var frames = new Dictionary<char, CapturedFrame>();
			foreach (var request in requests)
			{
				var animation = request.Animations.FirstOrDefault(a => sprite.HasAnimation(a));
				if (animation == null)
				{
					continue;
				}
				state.SetAnimation(animation, false);
				using (var entry = state.GetCurrent(0))
				{
					if (entry == null)
					{
						continue;
					}
					entry.SetMixDuration(0f);
					var end = entry.GetAnimationEnd();
					entry.SetTrackTime((float)Math.Clamp(request.Fraction * end, 0.0, Math.Max(0.0, end - 0.02)));
				}
				state.Update(0f);
				state.Apply(skeleton);

				viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
				await NextFrame(host);
				await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
				var pixels = PlayerFace.ToPixels(viewport.GetTexture().GetImage());
				if (pixels != null)
				{
					frames[request.Letter] = new CapturedFrame(pixels, (int)FeetX, (int)FeetY);
				}
			}
			return frames;
		}
		catch (Exception e)
		{
			ModLog.Fail("enemy", $"taking pictures of {name}", e);
			return null;
		}
		finally
		{
			if (viewport != null && GodotObject.IsInstanceValid(viewport))
			{
				viewport.QueueFree();
			}
		}
	}

	private static async Task NextFrame(Node host)
	{
		await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}
