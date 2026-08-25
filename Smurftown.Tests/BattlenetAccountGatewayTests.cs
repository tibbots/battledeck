using System.ComponentModel;
using System.IO;
using System.Linq;
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
            europe.RankPoints = 328;
            europe.RankPointsMax = 1000;
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
            Assert.Equal(328, back.RankPoints);
            Assert.Equal(1000, back.RankPointsMax);
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

        /// <summary>
        ///     The demo accounts the README images are taken against, read through the real
        ///     gateway.
        ///     <para>
        ///         <b>Where it would otherwise fail is expensive.</b> `tools/demo-data.yaml` is
        ///         generated by `tools/gen-demo-data.py` and used by `tools/test-home.ps1`; the
        ///         moment it no longer loads, that shows up as an app that will not start in
        ///         the middle of a capture run - with the real account list already moved
        ///         aside. Thirteen accounts, because a file that parses to nothing also parses.
        ///     </para>
        /// </summary>
        [Fact]
        public void The_demo_accounts_load()
        {
            var folder = FreshFolder();
            File.Copy("demo-data.yaml", Path.Combine(folder, "data.yaml"));

            var accounts = new BattlenetAccountGateway(folder).BattlenetAccounts;

            Assert.Equal(13, accounts.Count);
            Assert.All(accounts, account => Assert.EndsWith("@example.com", account.Email));
        }

        [Fact]
        public void The_written_file_says_which_layout_it_is_in()
        {
            var folder = FreshFolder();

            new BattlenetAccountGateway(folder).AddOrUpdate(Mail("smurf@example.com"));

            var written = File.ReadAllText(Path.Combine(folder, "data.yaml"));
            Assert.StartsWith($"schemaVersion: {BattlenetAccountGateway.CurrentSchema}", written);
            Assert.Contains("accounts:", written);
        }

        /// <summary>
        ///     The layout before 1.3.0: a bare sequence, no version in front of it. Every
        ///     installation up to that release has one, and it has to be read without a word
        ///     from the human.
        /// </summary>
        [Fact]
        public void A_file_of_the_older_layout_is_read_and_upgraded_by_the_next_save()
        {
            var folder = FreshFolder();
            File.WriteAllText(Path.Combine(folder, "data.yaml"),
                """
                # A comment before the first item, as the demo data has one.
                - name: OLDTIMER
                  discriminator: '1234'
                  email: oldtimer@example.com
                  password: whatever
                  notes: ''
                  latestInteractionAt: 2026-08-21T19:40:00
                  inactive: false
                """);

            var gateway = new BattlenetAccountGateway(folder);
            Assert.Equal("oldtimer@example.com", Assert.Single(gateway.BattlenetAccounts).Email);

            // Reading leaves the file alone; the upgrade rides along with the next change.
            Assert.StartsWith("#", File.ReadAllText(Path.Combine(folder, "data.yaml")));

            gateway.AddOrUpdate(Mail("second@example.com"));

            var written = File.ReadAllText(Path.Combine(folder, "data.yaml"));
            Assert.StartsWith($"schemaVersion: {BattlenetAccountGateway.CurrentSchema}", written);
            Assert.Contains("oldtimer@example.com", written);
            Assert.Contains("second@example.com", written);
        }

        [Fact]
        public void A_file_from_a_newer_schema_is_never_overwritten()
        {
            var folder = FreshFolder();
            var path = Path.Combine(folder, "data.yaml");
            File.WriteAllText(path,
                $"""
                 schemaVersion: {BattlenetAccountGateway.CurrentSchema + 1}
                 accounts: []
                 somethingFromTomorrow: tomorrow
                 """);

            var gateway = new BattlenetAccountGateway(folder);

            // Deserialising drops every key this build does not know. Writing the file back
            // would delete them - and this is the file with the passwords in it.
            Assert.Throws<InvalidOperationException>(() => gateway.AddOrUpdate(Mail("smurf@example.com")));
            Assert.Contains("tomorrow", File.ReadAllText(path));
        }

        [Fact]
        public void A_save_reads_the_file_again_and_does_not_answer_out_of_memory()
        {
            var folder = FreshFolder();
            var gateway = new BattlenetAccountGateway(folder);
            gateway.AddOrUpdate(Mail("first@example.com"));

            // Somebody else rewrites the file - another Smurftown, or an editor. This save
            // still overwrites it, deliberately: refusing would lock the human out of their
            // own edit. What must not happen is that it goes unnoticed, and the read below is
            // what notices.
            File.WriteAllText(Path.Combine(folder, "data.yaml"),
                $"schemaVersion: {BattlenetAccountGateway.CurrentSchema}{Environment.NewLine}accounts: []");

            gateway.AddOrUpdate(Mail("second@example.com"));

            var read = new BattlenetAccountGateway(folder).BattlenetAccounts;
            Assert.Equal(2, read.Count);
        }

        [Fact]
        public void An_empty_file_is_not_a_file_from_an_unknown_layout()
        {
            var folder = FreshFolder();

            // ensureConfigFileExists creates an empty one on the first read of a fresh
            // installation. Nothing about it says "older layout", and treating it as such
            // would make the first save log an upgrade that never happened.
            File.WriteAllText(Path.Combine(folder, "data.yaml"), "");

            Assert.Empty(new BattlenetAccountGateway(folder).BattlenetAccounts);
        }

        /// <summary>
        ///     The distinction the ring hangs on: an account at the start of its division
        ///     has zero points, an unread one has none. Both draw an untouched medal, so the
        ///     only place the difference survives is the file - and a round trip that turned
        ///     null into 0 would put a reading on screen that nobody took.
        /// </summary>
        [Fact]
        public void Unread_rank_points_come_back_as_null_and_not_as_zero()
        {
            var folder = FreshFolder();

            var written = Mail("nopoints@example.com");
            var europe = written.HotsFor(BattlenetRegion.Europe);
            europe.Tier = HotsRankTier.Gold;
            europe.Division = 3;

            new BattlenetAccountGateway(folder).AddOrUpdate(written);

            var back = Assert.Single(new BattlenetAccountGateway(folder).BattlenetAccounts)
                .HotsFor(BattlenetRegion.Europe);

            Assert.Null(back.RankPoints);
            Assert.Null(back.RankPointsMax);
            Assert.Null(back.RankProgress);
        }

        /// <summary>
        ///     Zero points ARE a reading, and one that has to survive - it is what the first
        ///     game of a new division writes.
        /// </summary>
        [Fact]
        public void Zero_rank_points_survive_as_zero()
        {
            var folder = FreshFolder();

            var written = Mail("freshdivision@example.com");
            var europe = written.HotsFor(BattlenetRegion.Europe);
            europe.Tier = HotsRankTier.Gold;
            europe.Division = 3;
            europe.RankPoints = 0;
            europe.RankPointsMax = 1000;

            new BattlenetAccountGateway(folder).AddOrUpdate(written);

            var back = Assert.Single(new BattlenetAccountGateway(folder).BattlenetAccounts)
                .HotsFor(BattlenetRegion.Europe);

            Assert.Equal(0, back.RankPoints);
            Assert.Equal(0.0, back.RankProgress);
        }

        /// <summary>
        ///     "Unranked" in the filter covers both cases the row itself doesn't tell apart:
        ///     never read at all (<c>HotsByRegion</c> has no entry, <c>Hots</c> is null) and
        ///     read once with no tier ever set. Both must match a filter narrowed to
        ///     <see cref="HotsRankTier.None" />, and neither must match a filter narrowed to a
        ///     real tier.
        /// </summary>
        [Fact]
        public void Rank_filter_matches_the_effective_tier_including_both_unranked_cases()
        {
            var folder = FreshFolder();
            var gateway = new BattlenetAccountGateway(folder);

            var gold = Mail("gold@example.com");
            gold.HotsFor(BattlenetRegion.Europe).Tier = HotsRankTier.Gold;
            gateway.AddOrUpdate(gold);

            // Never touches HotsFor - HotsByRegion stays empty, Hots is null.
            gateway.AddOrUpdate(Mail("neverread@example.com"));

            var readNoRank = Mail("readnorank@example.com");
            readNoRank.HotsFor(BattlenetRegion.Europe).ReadAt = DateTime.Now;
            gateway.AddOrUpdate(readNoRank);

            gateway.FilterBy("", Games.Hots, BattlenetRegion.Europe, [], [], [HotsRankTier.None], false);
            var unranked = gateway.AccountRegionsFiltered.Cast<AccountRegion>()
                .Select(row => row.Account.Email).ToList();

            Assert.Contains("neverread@example.com", unranked);
            Assert.Contains("readnorank@example.com", unranked);
            Assert.DoesNotContain("gold@example.com", unranked);

            gateway.FilterBy("", Games.Hots, BattlenetRegion.Europe, [], [], [HotsRankTier.Gold], false);
            Assert.Equal(["gold@example.com"],
                gateway.AccountRegionsFiltered.Cast<AccountRegion>().Select(row => row.Account.Email));
        }

        /// <summary>
        ///     A never-read row has no gold to compare, so it has to sort as if it had less
        ///     than any account that HAS a reading - including one that read zero.
        /// </summary>
        [Fact]
        public void Sorting_by_gold_puts_a_never_read_account_below_every_real_amount()
        {
            var folder = FreshFolder();
            var gateway = new BattlenetAccountGateway(folder);

            var rich = Mail("rich@example.com");
            rich.HotsFor(BattlenetRegion.Europe).Gold = 5000;
            gateway.AddOrUpdate(rich);

            var broke = Mail("broke@example.com");
            broke.HotsFor(BattlenetRegion.Europe).Gold = 0;
            gateway.AddOrUpdate(broke);

            gateway.AddOrUpdate(Mail("neverread2@example.com"));

            gateway.SortBy(AccountSortField.Gold, ListSortDirection.Descending);

            Assert.Equal(
                ["rich@example.com", "broke@example.com", "neverread2@example.com"],
                gateway.AccountRegionsFiltered.Cast<AccountRegion>().Select(row => row.Account.Email));
        }

        /// <summary>
        ///     The one thing <see cref="BattlenetAccountGateway.FindByBattletag" /> cannot say
        ///     on its own - it answers "zero" and "more than one" identically with <c>null</c>.
        ///     This is what <c>RunGuideViewModel</c> asks instead, so an ambiguous battletag
        ///     fails the running-client read instead of getting a third account created on top
        ///     of the two that already collide.
        /// </summary>
        [Fact]
        public void Ambiguous_battletag_is_reported_and_a_unique_one_is_not()
        {
            var folder = FreshFolder();
            var gateway = new BattlenetAccountGateway(folder);

            var first = Account("first@example.com");
            first.Name = "PITAPAN";
            first.Discriminator = "2523";
            gateway.AddOrUpdate(first);

            Assert.False(gateway.IsAmbiguousBattletag("PITAPAN#2523"));
            Assert.NotNull(gateway.FindByBattletag("PITAPAN#2523"));

            var second = Account("second@example.com");
            second.Name = "PITAPAN";
            second.Discriminator = "2523";
            gateway.AddOrUpdate(second);

            Assert.True(gateway.IsAmbiguousBattletag("PITAPAN#2523"));
            Assert.Null(gateway.FindByBattletag("PITAPAN#2523"));
        }

        [Fact]
        public void An_unseen_battletag_is_not_ambiguous()
        {
            var folder = FreshFolder();
            var gateway = new BattlenetAccountGateway(folder);
            gateway.AddOrUpdate(Mail("known@example.com"));

            Assert.False(gateway.IsAmbiguousBattletag("NOBODY#1111"));
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
