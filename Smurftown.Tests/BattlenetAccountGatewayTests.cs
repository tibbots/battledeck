using System.IO;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Xunit;

namespace Smurftown.Tests
{
    /// <summary>
    ///     The account list through a full write and read.
    ///     <para>
    ///         <b>The failure this is aimed at does not throw.</b> If YamlDotNet writes a
    ///         field it then does not recognise on reading, nothing goes wrong visibly - the
    ///         value is simply gone, and an empty list looks exactly like one that was never
    ///         read. Until it was deleted, a <c>VerifyMigration</c> guarded that at run time
    ///         for the one migration that existed. As a test it costs nothing and covers
    ///         every change to the entity, not one migration.
    ///     </para>
    ///     <para>
    ///         Every case gets a folder of its own. The gateway rewrites the whole file on
    ///         every mutation and holds no lock, so two cases sharing a folder would be a
    ///         coin flip.
    ///     </para>
    /// </summary>
    public class BattlenetAccountGatewayTests
    {
        [Fact]
        public void An_account_survives_a_write_and_a_read()
        {
            var folder = FreshFolder();

            var written = Account("smurf@example.com");
            written.SetRegions(Games.Hots, [BattlenetRegion.Europe, BattlenetRegion.Americas]);
            written.SetRegions(Games.Overwatch, [BattlenetRegion.Europe]);

            var europe = written.HotsFor(BattlenetRegion.Europe);
            europe.Tier = HotsRankTier.Platinum;
            europe.Division = 2;
            europe.Heroes = ["tracer", "muradin"];
            europe.Gold = 12480;
            europe.LootChests = 6;

            new BattlenetAccountGateway(folder).AddOrUpdate(written);

            // A SECOND gateway, not a Reload on the first: reading back through the same
            // instance would be answered out of the list it still holds in memory, and the
            // file - the thing under test - would never be touched.
            var read = Assert.Single(new BattlenetAccountGateway(folder).BattlenetAccounts);

            Assert.Equal("smurf@example.com", read.Email);
            Assert.Equal(new[] { BattlenetRegion.Europe, BattlenetRegion.Americas },
                read.RegionsFor(Games.Hots));
            Assert.Equal(new[] { BattlenetRegion.Europe }, read.RegionsFor(Games.Overwatch));

            var back = read.HotsFor(BattlenetRegion.Europe);
            Assert.Equal(HotsRankTier.Platinum, back.Tier);
            Assert.Equal(2, back.Division);
            Assert.Equal(new[] { "muradin", "tracer" }, back.Heroes.Order());
            Assert.Equal(12480, back.Gold);
            Assert.Equal(6, back.LootChests);

            // The other region was never touched, so it must not have appeared out of the
            // round trip - "played there" and "has something in it" are two different things.
            Assert.Null(read.HotsIn(BattlenetRegion.Americas));
        }

        /// <summary>
        ///     The point of the folder being a constructor argument: two gateways in one
        ///     process work on two files. Before that it was a static resolved once, and this
        ///     test could not be written at all.
        /// </summary>
        [Fact]
        public void Two_gateways_on_different_folders_do_not_see_each_other()
        {
            var here = FreshFolder();
            var there = FreshFolder();

            new BattlenetAccountGateway(here).AddOrUpdate(Mail("here@example.com"));
            new BattlenetAccountGateway(there).AddOrUpdate(Mail("there@example.com"));

            Assert.Equal("here@example.com",
                Assert.Single(new BattlenetAccountGateway(here).BattlenetAccounts).Email);
            Assert.Equal("there@example.com",
                Assert.Single(new BattlenetAccountGateway(there).BattlenetAccounts).Email);
        }

        /// <summary>
        ///     An account with no game in any region has no row - and could then never be
        ///     repaired by hand either, because the edit button sits in the row that does not
        ///     exist. A hand-edited file is how it happens.
        /// </summary>
        [Fact]
        public void An_account_without_a_region_is_repaired_on_read()
        {
            var folder = FreshFolder();
            File.WriteAllText(Path.Combine(folder, "data.yaml"),
                """
                - name: ORPHAN
                  discriminator: '1234'
                  email: orphan@example.com
                  password: whatever
                  notes: ''
                  latestInteractionAt: 2026-08-21T19:40:00
                  inactive: false
                """);

            var read = Assert.Single(new BattlenetAccountGateway(folder).BattlenetAccounts);

            Assert.Equal(new[] { BattlenetRegion.Europe }, read.RegionsFor(Games.Hots));

            // Repaired in memory and NOT written back: a start that rewrites the file it was
            // only asked to read changes data nobody asked it to change. The next edit to
            // that account saves it anyway.
            Assert.DoesNotContain("regionsByGame",
                File.ReadAllText(Path.Combine(folder, "data.yaml")));
        }

        private static string FreshFolder()
        {
            var folder = Path.Combine(TestHome.Path, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static BattlenetAccount Mail(string email)
        {
            var account = Account(email);
            account.SetRegions(Games.Hots, [BattlenetRegion.Europe]);
            return account;
        }

        private static BattlenetAccount Account(string email) => new()
        {
            Name = "",
            Discriminator = "",
            Email = email,
            Password = "demo-pass",
            Notes = "",
            LatestInteractionAt = new DateTime(2026, 8, 21, 19, 40, 0)
        };
    }
}
