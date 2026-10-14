using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Optional extension contract reserved for a future separate Asgard mod.
    /// WNG deliberately owns no Asgard package IDs, factions, pawn kinds, technologies or content.
    /// A separate mod may register a provider at runtime if it wants WNG to recognize its own Defs.
    /// Provider instances are process-local and are never serialized into a save.
    /// </summary>
    public interface IWNGAsgardCompatibilityProvider
    {
        string ProviderId { get; }
        bool IsAsgardFaction(Faction faction);
        bool IsAsgardPawn(Pawn pawn);
        bool IsAsgardDef(Def def);
    }

    /// <summary>
    /// Dependency-free recognition surface for a future external Asgard implementation.
    /// No behavior changes merely because a provider is registered; future WNG features must query
    /// this surface explicitly and remain correct when no provider exists.
    /// </summary>
    public static class WNGAsgardCompatibility
    {
        private static readonly object Sync = new object();
        private static readonly List<IWNGAsgardCompatibilityProvider> Providers =
            new List<IWNGAsgardCompatibilityProvider>();

        public static bool HasProviders
        {
            get
            {
                lock (Sync)
                    return Providers.Count > 0;
            }
        }

        public static IReadOnlyList<string> RegisteredProviderIds
        {
            get
            {
                lock (Sync)
                    return Providers
                        .Where(p => p != null && !p.ProviderId.NullOrEmpty())
                        .Select(p => p.ProviderId)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToList();
            }
        }

        public static bool RegisterProvider(IWNGAsgardCompatibilityProvider provider)
        {
            if (provider == null || provider.ProviderId.NullOrEmpty())
                return false;

            lock (Sync)
            {
                int existing = Providers.FindIndex(p =>
                    p != null &&
                    string.Equals(p.ProviderId, provider.ProviderId, StringComparison.Ordinal));
                if (existing >= 0)
                {
                    if (ReferenceEquals(Providers[existing], provider))
                        return true;
                    Providers[existing] = provider;
                    return true;
                }

                Providers.Add(provider);
                return true;
            }
        }

        public static bool UnregisterProvider(string providerId)
        {
            if (providerId.NullOrEmpty())
                return false;

            lock (Sync)
            {
                int removed = Providers.RemoveAll(p =>
                    p == null ||
                    string.Equals(p.ProviderId, providerId, StringComparison.Ordinal));
                return removed > 0;
            }
        }

        public static bool IsAsgardFaction(Faction faction)
        {
            if (faction == null)
                return false;
            return AnyProvider(
                provider => provider.IsAsgardFaction(faction),
                "faction recognition");
        }

        public static bool IsAsgardPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return false;
            return AnyProvider(
                provider => provider.IsAsgardPawn(pawn),
                "pawn recognition");
        }

        public static bool IsAsgardDef(Def def)
        {
            if (def == null)
                return false;
            return AnyProvider(
                provider => provider.IsAsgardDef(def),
                "Def recognition");
        }

        private static bool AnyProvider(
            Func<IWNGAsgardCompatibilityProvider, bool> predicate,
            string operation)
        {
            IWNGAsgardCompatibilityProvider[] snapshot;
            lock (Sync)
                snapshot = Providers.Where(p => p != null).ToArray();

            foreach (IWNGAsgardCompatibilityProvider provider in snapshot)
            {
                try
                {
                    if (predicate(provider))
                        return true;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[WNG] Optional Asgard compatibility provider '" +
                        (provider.ProviderId ?? "<unnamed>") +
                        "' failed during " + operation + ": " + ex.Message);
                }
            }
            return false;
        }
    }
}
