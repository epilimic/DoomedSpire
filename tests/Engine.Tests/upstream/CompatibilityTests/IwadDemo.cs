using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedDoom;

namespace ManagedDoomTest.CompatibilityTests
{
    [TestClass]
    public class IwadDemo
    {
        [TestMethod]
        public void Doom1SharewareDemo1()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom1Shareware))
            {
                var demo = new Demo(content.Wad.ReadLump("DEMO1"));
                var cmds = Enumerable.Range(0, Player.MaxPlayerCount).Select(i => new TicCmd()).ToArray();
                var game = new DoomGame(content, demo.Options);
                game.DeferedInitNew();

                var lastMobjHash = 0;
                var aggMobjHash = 0;
                var lastSectorHash = 0;
                var aggSectorHash = 0;

                while (true)
                {
                    if (!demo.ReadCmd(cmds))
                    {
                        break;
                    }

                    game.Update(cmds);
                    lastMobjHash = DoomDebug.GetMobjHash(game.World);
                    aggMobjHash = DoomDebug.CombineHash(aggMobjHash, lastMobjHash);
                    lastSectorHash = DoomDebug.GetSectorHash(game.World);
                    aggSectorHash = DoomDebug.CombineHash(aggSectorHash, lastSectorHash);
                }

                Assert.AreEqual(0xa497cb7fu, (uint)lastMobjHash);
                Assert.AreEqual(0x5a1776fdu, (uint)aggMobjHash);
                Assert.AreEqual(0x55d373a2u, (uint)lastSectorHash);
                Assert.AreEqual(0xcaafd23bu, (uint)aggSectorHash);
            }
        }

        [TestMethod]
        public void Doom1SharewareDemo2()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom1Shareware))
            {
                var demo = new Demo(content.Wad.ReadLump("DEMO2"));
                var cmds = Enumerable.Range(0, Player.MaxPlayerCount).Select(i => new TicCmd()).ToArray();
                var game = new DoomGame(content, demo.Options);
                game.DeferedInitNew();

                var lastMobjHash = 0;
                var aggMobjHash = 0;
                var lastSectorHash = 0;
                var aggSectorHash = 0;

                while (true)
                {
                    if (!demo.ReadCmd(cmds))
                    {
                        break;
                    }

                    game.Update(cmds);
                    lastMobjHash = DoomDebug.GetMobjHash(game.World);
                    aggMobjHash = DoomDebug.CombineHash(aggMobjHash, lastMobjHash);
                    lastSectorHash = DoomDebug.GetSectorHash(game.World);
                    aggSectorHash = DoomDebug.CombineHash(aggSectorHash, lastSectorHash);
                }

                Assert.AreEqual(0xf7f5daddu, (uint)lastMobjHash);
                Assert.AreEqual(0xb576525au, (uint)aggMobjHash);
                Assert.AreEqual(0xf2e936b0u, (uint)lastSectorHash);
                Assert.AreEqual(0xe62009fau, (uint)aggSectorHash);
            }
        }

        [TestMethod]
        public void Doom1SharewareDemo3()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom1Shareware))
            {
                var demo = new Demo(content.Wad.ReadLump("DEMO3"));
                var cmds = Enumerable.Range(0, Player.MaxPlayerCount).Select(i => new TicCmd()).ToArray();
                var game = new DoomGame(content, demo.Options);
                game.DeferedInitNew();

                var lastMobjHash = 0;
                var aggMobjHash = 0;
                var lastSectorHash = 0;
                var aggSectorHash = 0;

                while (true)
                {
                    if (!demo.ReadCmd(cmds))
                    {
                        break;
                    }

                    game.Update(cmds);
                    lastMobjHash = DoomDebug.GetMobjHash(game.World);
                    aggMobjHash = DoomDebug.CombineHash(aggMobjHash, lastMobjHash);
                    lastSectorHash = DoomDebug.GetSectorHash(game.World);
                    aggSectorHash = DoomDebug.CombineHash(aggSectorHash, lastSectorHash);
                }

                Assert.AreEqual(0x893f32d2u, (uint)lastMobjHash);
                Assert.AreEqual(0x22b21b86u, (uint)aggMobjHash);
                Assert.AreEqual(0xfef34aafu, (uint)lastSectorHash);
                Assert.AreEqual(0xa881ce6fu, (uint)aggSectorHash);
            }
        }

    }
}
