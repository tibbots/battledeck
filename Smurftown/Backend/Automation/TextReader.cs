using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

// Windows.Graphics.Imaging and System.Windows.Media.Imaging carry several type names
// twice - BitmapDecoder, BitmapEncoder, BitmapFrame. This file needs both worlds, so
// every ambiguous name here gets an alias instead of a full qualification in the
// middle of the code. Whoever uses another image type checks it against this list.
using WinRtDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using WpfBitmapFrame = System.Windows.Media.Imaging.BitmapFrame;

namespace Smurftown.Backend.Automation
{
    /// <summary>A recognized text line along with its position in the given crop.</summary>
    public sealed record TextLine(string Text, int X, int Y, int Width, int Height);

    /// <summary>
    ///     Reads text from an image crop - with the text recognition built into Windows.
    ///     <para>
    ///         <b>Why text at all and not image comparison:</b> the collection writes every
    ///         hero name as text under the card, and the header bar writes gold, shards,
    ///         gems, and account level as a number. Via comparison images this would need 90
    ///         name templates plus digit templates - and each of those would first have to
    ///         come from somewhere. Reading it is done in one step.
    ///     </para>
    ///     <para>
    ///         <b>Why Windows and no package:</b> <c>Windows.Media.Ocr</c> lives in the
    ///         system, the language packs too. There is nothing to ship and nothing to
    ///         update. The price is the target framework
    ///         <c>net8.0-windows10.0.19041.0</c> in the csproj.
    ///     </para>
    ///     <para>
    ///         <b>Keep crops small.</b> Measured: on large images the recognition
    ///         occasionally returns nothing at all, without error - on small ones it is
    ///         error-free instead. Hero names are therefore read per card individually and
    ///         not the whole card field at once. This is not polish, but the difference
    ///         between "reads" and "does not read".
    ///     </para>
    ///     <para>
    ///         <b>Errors are expected.</b> The recognition delivers "Arth"" instead of
    ///         "Arthas" and "Funke/chen" instead of "Funkelchen". That is not a problem as
    ///         long as the answer comes from a known list - see
    ///         <see cref="HeroNameMatcher" />. Where there is no such list (numbers), parsing
    ///         is strict and nothing is taken over in case of doubt.
    ///     </para>
    /// </summary>
    public static class TextReader
    {
        /// <summary>
        ///     The language the recognition is set up with - a BCP-47 tag like <c>de</c> or
        ///     <c>fr</c>. Publicly writable and set <b>from outside</b>
        ///     (<c>SettingsGateway.Apply</c>), exactly like <see cref="InputSender.Pace" />
        ///     and <see cref="GameVocabulary.Current" /> and for the same reason:
        ///     <c>Backend/Automation</c> does not know the gateways.
        ///     <para>
        ///         Until 22.08.2026 a <c>const string = "de"</c> stood here. The English
        ///         version could still be read with it - a German recognizer copes with
        ///         Latin script, the language model only helps with ambiguities. With French
        ///         and Spanish this weighs more heavily, because accented letters are
        ///         frequent there.
        ///     </para>
        /// </summary>
        public static string LanguageTag
        {
            get => _languageTag;
            set
            {
                if (_languageTag == value) return;
                _languageTag = value;

                // Without this discard, the once-built recognizer would survive a language
                // change and keep silently reading with the wrong model afterward - the
                // same trap as with HeroNameMatcher, and just as inconspicuous: it shows up
                // neither when translating nor in the log.
                _engine = null;
                _engineTried = false;
            }
        }

        private static string _languageTag = "de";

        private static OcrEngine? _engine;
        private static bool _engineTried;

        /// <summary>
        ///     Whether reading is possible on this machine at all. If the language pack is
        ///     missing, that is not a crash but a missing feature - rank and login keep
        ///     working.
        /// </summary>
        public static bool Available => Engine() != null;

        private static OcrEngine? Engine()
        {
            if (_engineTried) return _engine;
            _engineTried = true;

            var wanted = _languageTag;
            _engine = OcrEngine.TryCreateFromLanguage(new Language(wanted));

            if (_engine != null)
            {
                Log.Information("Text recognition running with {Language}",
                    _engine.RecognizerLanguage.LanguageTag);
                return _engine;
            }

            // The fallback stays, but it is NAMED. A language pack is installed per
            // language, and on a machine that only brings German, the German recognizer
            // still reads French labels - but measurably worse with accents. Whoever later
            // misses a word should see in the log that the requested language was not the
            // one actually read.
            _engine = OcrEngine.TryCreateFromUserProfileLanguages();

            if (_engine == null)
                Log.Warning("No text recognition available - neither for '{Wanted}' nor for the " +
                            "languages of the user profile. Heroes and stats stay unread.", wanted);
            else
                Log.Warning("No text recognition for '{Wanted}' - falling back to {Fallback}. " +
                            "Accented characters will read worse. Install the Windows language " +
                            "feature for '{Wanted}' to fix this.",
                    wanted, _engine.RecognizerLanguage.LanguageTag);

            return _engine;
        }

        /// <summary>
        ///     Reads a crop. <paramref name="upscale" /> enlarges it beforehand - small text
        ///     is read noticeably more reliably as a result, and at the crop sizes used here
        ///     it costs nothing.
        /// </summary>
        public static async Task<IReadOnlyList<TextLine>> ReadAsync(
            Screenshot shot, int x, int y, int width, int height, int upscale = 1)
        {
            var engine = Engine();
            if (engine == null) return [];

            using var bitmap = await ToSoftwareBitmap(shot.Crop(x, y, width, height), upscale);
            var result = await engine.RecognizeAsync(bitmap);

            var lines = new List<TextLine>();
            foreach (var line in result.Lines)
            {
                double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
                foreach (var word in line.Words)
                {
                    var box = word.BoundingRect;
                    left = Math.Min(left, box.X);
                    top = Math.Min(top, box.Y);
                    right = Math.Max(right, box.X + box.Width);
                    bottom = Math.Max(bottom, box.Y + box.Height);
                }

                if (right <= left) continue;
                lines.Add(new TextLine(line.Text,
                    (int)Math.Round(left / upscale), (int)Math.Round(top / upscale),
                    (int)Math.Round((right - left) / upscale), (int)Math.Round((bottom - top) / upscale)));
            }

            return lines;
        }

        /// <summary>The whole text of a crop, lines separated by spaces.</summary>
        public static async Task<string> ReadTextAsync(
            Screenshot shot, int x, int y, int width, int height, int upscale = 1)
        {
            var lines = await ReadAsync(shot, x, y, width, height, upscale);
            return string.Join(" ", lines.Select(l => l.Text));
        }

        /// <summary>
        ///     The path from our own capture buffer to what the recognition expects: write
        ///     it as a PNG into a memory stream and decode it again there.
        ///     <para>
        ///         The detour via PNG looks like waste but is the only way without
        ///         <c>unsafe</c>: otherwise you only get at your own image buffer through
        ///         <c>IMemoryBufferByteAccess</c> and pointer arithmetic into the
        ///         <c>SoftwareBitmap</c>. For a handful of crops per login, the encoding does
        ///         not weigh in.
        ///     </para>
        /// </summary>
        private static async Task<SoftwareBitmap> ToSoftwareBitmap(Screenshot crop, int upscale)
        {
            BitmapSource source = crop.ToBitmap();
            if (upscale > 1)
            {
                var scaled = new TransformedBitmap(source, new ScaleTransform(upscale, upscale));
                scaled.Freeze();
                source = scaled;
            }

            using var memory = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(WpfBitmapFrame.Create(source));
            encoder.Save(memory);

            using var stream = new InMemoryRandomAccessStream();
            var writer = new DataWriter(stream);
            writer.WriteBytes(memory.ToArray());
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);

            var decoder = await WinRtDecoder.CreateAsync(stream);
            var decoded = await decoder.GetSoftwareBitmapAsync();

            // RecognizeAsync insists on Bgra8 with premultiplied or ignored alpha. What the
            // PNG decoder delivers depends on the file - so check instead of hope.
            if (decoded.BitmapPixelFormat == BitmapPixelFormat.Bgra8 &&
                decoded.BitmapAlphaMode != BitmapAlphaMode.Straight)
                return decoded;

            using (decoded)
            {
                return SoftwareBitmap.Convert(decoded, BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);
            }
        }
    }
}
