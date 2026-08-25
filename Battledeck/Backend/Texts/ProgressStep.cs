namespace Battledeck.Backend.Texts
{
    /// <summary>
    ///     One line of progress from a game run - a text key and the values it takes, not yet
    ///     rendered into a string.
    ///     <para>
    ///         <b>Replaces a plain <c>string</c> on 24.08.2026.</b> Until then
    ///         <c>IProgress&lt;string&gt;</c> carried a finished line, rendered once via
    ///         <see cref="Strings.ForLog" /> - correct as long as <c>smurftown.log</c> was the
    ///         only subscriber, since a log stays English on purpose. Then
    ///         <c>RunGuideViewModel</c> and <c>ReuseGuideViewModel</c> started showing the same
    ///         channel to the human behind the funnel, and a line already rendered into English
    ///         cannot become German again. Carrying the key instead lets each reader render its
    ///         own way off the same step: the log through <see cref="Strings.ForLog" />, the
    ///         funnel through <see cref="Strings.Format" />.
    ///     </para>
    /// </summary>
    public readonly record struct ProgressStep(string Key, object?[] Args)
    {
        public static ProgressStep Of(string key, params object?[] args) => new(key, args);
    }
}
