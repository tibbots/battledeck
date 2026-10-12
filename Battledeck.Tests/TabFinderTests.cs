using System.IO;
using Battledeck.Backend.Automation;
using Xunit;

namespace Battledeck.Tests
{
    public class TabFinderTests
    {
        [Theory]
        [InlineData("collection", "tab-not-found-collection")]
        [InlineData("ARAM", "tab-not-found-aram")]
        [InlineData("hero packs", "tab-not-found-hero-packs")]
        [InlineData("a/b\\c:d*e?", "tab-not-found-a-b-c-d-e-")]
        [InlineData("héros", "tab-not-found-h-ros")]
        public void The_reason_names_the_tab_and_fits_into_a_file_name(string what, string expected)
        {
            var reason = TabFinder.DiagnosticReason(what);

            Assert.Equal(expected, reason);
            Assert.Equal(-1, reason.IndexOfAny(Path.GetInvalidFileNameChars()));
        }
    }
}
