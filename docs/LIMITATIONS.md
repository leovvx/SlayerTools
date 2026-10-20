# Limitations (by design)

Slayer Tools is for tinkering with **your own single-player progress**. It deliberately stays away from anything involving real money or the developer's servers.

## Read-only stats
Any stat whose name contains `jewelsofsoul`, `purchase`, `adswatched` or `starterpack`, plus `initialized`, is shown **read-only** in the editors. This is done by `Blocked()` in `src/Plugin.cs`:

```csharp
private static bool Blocked(string n)
{
    n = n.ToLowerInvariant();
    return n.Contains("jewelsofsoul") || n.Contains("purchase") || n.Contains("adswatched") || n.Contains("starterpack") || n == "initialized";
}
```

`Collect()` marks every matching property as `ReadOnly`, and every write path (`SetProp`, `ApplyTo`, presets) refuses read-only stats. **Jewels of Soul** are the game's paid currency.

## No online transactions
The mod never calls purchase, ad-reward or cloud-sync code, and never edits anything the game sends to or receives from its servers.

## No casino cheats
The roulette, the Slayers Machine (slots) and the casino cashier all go through the game's **server**: `CasinoManager.PlayRoll`, `SpendChips`, `BuyItem`… are asynchronous calls with success / not-enough-chips / error callbacks. Chips are stored server-side, the server draws the result, and chips can also be bought with real money.

Changing the chip balance or forcing a colour (red / black / green) would mean cheating against that server and the paid economy, and could get the account banned. The mod does not do it and will not.

What is still possible: the local counters `rouletteWins` / `rouletteLosses` (Inventory editor), and bag resources.

## Other
- No bypassing of other platforms' protections (iOS / macOS builds).
- No auto-farm (playing the game for you).
