using System.Windows.Data;
using System.Windows.Markup;
using Smurftown.Backend.Texts;

namespace Smurftown.UI.MVVM
{
    /// <summary>
    ///     Gets a translated text into the XAML: <c>Text="{loc:Str main.tabAccounts}"</c>.
    ///     <para>
    ///         <b>No string is delivered, but a binding.</b> That is the
    ///         whole trick, and the runtime language switch depends on it: if the
    ///         extension returned the finished text, it would be fixed from the moment the view loads -
    ///         a change in the settings would never reach it, and the application
    ///         would have to be restarted. The binding instead points to the indexer of
    ///         <see cref="Strings.Current" />; when that one reports a change on <c>Item[]</c>, every
    ///         binding across the whole window re-reads it.
    ///     </para>
    ///     <para>
    ///         <b>That is why the instance is never swapped</b> - see
    ///         <see cref="Strings.Current" />. A new instance per language would leave all already
    ///         built bindings hanging on the old one, and silently at that.
    ///     </para>
    ///     <para>
    ///         <b>The key must not contain a comma.</b> It is placed into a binding path
    ///         of the form <c>[key]</c>, and there a comma separates several
    ///         indexer arguments. Dots are allowed and explicitly wanted: the
    ///         keys are flat and grouped by view (<c>dialog.save</c>), so that
    ///         the same text in the code and in the YAML file is literally the same and can
    ///         be found with a single search.
    ///     </para>
    ///     <para>
    ///         It lives <b>here</b> and not next to <see cref="Strings" />, because it needs WPF
    ///         - <c>System.Windows.Markup</c> - and the dictionary keeps itself free
    ///         of that.
    ///     </para>
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class StrExtension : MarkupExtension
    {
        public StrExtension()
        {
        }

        public StrExtension(string key)
        {
            Key = key;
        }

        /// <summary>The key, exactly as it stands in <c>Backend/Language/*.yaml</c>.</summary>
        public string Key { get; set; } = "";

        public override object? ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding($"[{Key}]")
            {
                Source = Strings.Current,
                Mode = BindingMode.OneWay
            };

            // Return ProvideValue and not the binding itself: only that way can the
            // extension be used at EVERY place - on a property just as in a
            // setter or a DataTemplate. A raw binding object would, wherever WPF
            // does not expect a binding expression, stand there as the text of its class name.
            return binding.ProvideValue(serviceProvider);
        }
    }
}
