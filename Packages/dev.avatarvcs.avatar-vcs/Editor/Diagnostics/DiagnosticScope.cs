using System;
using AvatarVcs.Core.Diagnostics;

namespace AvatarVcs.Editor.Diagnostics
{
    /// <summary>
    /// The "own it or borrow it" handling every DiagnosticLog-taking entry
    /// point needs (KAN-20), in one place.
    ///
    /// A caller mid-operation -- a commit collecting every tracked target, a
    /// checkout restoring every container -- passes its own log so the
    /// warnings all land in one place and reach CheckoutResult.Diagnostics.
    /// A direct caller, which in practice means a test, passes none; then the
    /// entry point makes one and is responsible for flushing it to the
    /// console.
    ///
    /// Written out by hand this is five lines and a try/finally per entry
    /// point, which is also why nine of them ended up split into an
    /// XxxCore method purely to have something for the try block to call.
    /// </summary>
    public readonly struct DiagnosticScope : IDisposable
    {
        private readonly DiagnosticLog owned;

        private DiagnosticScope(DiagnosticLog owned) => this.owned = owned;

        /// <summary>
        /// Leaves log alone when the caller supplied one; creates one when
        /// they didn't, and flushes exactly that one when the scope ends.
        ///
        /// Flushing happens on the exception path too -- warnings collected
        /// before something threw are usually the ones that explain it.
        /// </summary>
        public static DiagnosticScope OwnOrBorrow(ref DiagnosticLog log)
        {
            if (log != null) return new DiagnosticScope(null);

            log = new DiagnosticLog();
            return new DiagnosticScope(log);
        }

        /// <summary>
        /// For a top-level entry point that always makes its own log --
        /// Checkout, Commit -- rather than accepting one. Same flush
        /// guarantee, so every log in the codebase reaches the console
        /// through this one type and no call site can forget to flush.
        /// </summary>
        public static DiagnosticScope Own(out DiagnosticLog log)
        {
            log = new DiagnosticLog();
            return new DiagnosticScope(log);
        }

        public void Dispose()
        {
            if (owned != null) UnityDiagnosticSink.Flush(owned);
        }
    }
}
