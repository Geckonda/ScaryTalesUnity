# ScaryTales Refactor Plan

> **Status: the goal is met.** Phases 0–6 are done and every Phase 6 exit criterion was verified on a live run. **The game is deployed and playable over the internet** — a dedicated server in Docker on the owner's own VM, players create a room, read a 4-letter code to a friend, and play. No VPN tool, no port forwarding, nobody types an IP. That was the point of the whole exercise.
>
> Deployment lives in [deploy/](deploy/) (Dockerfile, compose file, runbook). Transport is KCP — raw UDP on 7777; there is no HTTP anywhere in it and no reverse proxy.
>
> **What is left is no longer refactoring.** See "Remaining work" at the bottom: some housekeeping found along the way, Phase 5's two real leftovers, and product decisions that were always out of scope.
>
> **6.4 was split** because the create/join messages are useless without the client work in 6.6, and the plan's own rule is that every step ships a playable build. 6.4a was the structural half (`Room` exists, the engine is off `NetworkBehaviour`) with no behavior change; 6.4b + 6.6 added codes and the join flow together, since neither half is usable alone.
> **Companion docs:** [docs/CARDS.md](docs/CARDS.md) (behaviour baseline for all 18 cards), [Architecture summary](C:/Users/yaloz/.claude/plans/i-have-a-unity-delightful-puppy.md) (frozen snapshot of the pre-refactor design; predates Phase 3, read as history).
>
> **Reading order for a fresh session:** this header → [Decisions locked in](#decisions-locked-in) → the Phase 6 section. The per-phase sections below the Progress list are the *original* plan as written before the work; where they disagree with Progress, **Progress wins**.

## Context

ScaryTales is a Mirror-networked card game. *Written when it supported 2 players on the happy path:* the codebase had grown coupled — a god-class `UnGameManager`, ~12 singletons, client-side-only authorization, and card effects that `await`ed player input directly, which forced `IPlayerInput` to leak into the core and `NetworkPlayerInput` to use deadlock-prone `TaskCompletionSource`s.

**As of 2026-08-21 all of that is resolved:** 2–4 players, server-authoritative, effects go through `IDecisionRouter`, the singleton zoo is gone. The remaining gap is *where the server runs* — see the scope change below.

This plan reorganizes the project to:
- Support **2-4 players in one session**.
- Make multiplayer **server-authoritative**: exactly one process runs the engine; clients are renderers.
- Decouple effects from input by making them produce **decision requests** instead of awaiting input.

Migration is **incremental**. Every phase lands in a working state — if we stop after any phase, you still have a shippable game.

> **Scope change, 2026-08-21.** Phases 0–5 were built for the **host model** (one player hosts, others join by IP). That shipped and works. It does not, however, let ordinary people play together over the internet without a VPN tool like Hamachi, which is the actual product requirement. **Phase 6 moves to a self-hosted dedicated server with multiple concurrent rooms.** Wherever this document still says "host" as the place the engine runs, read it as historical — the engine location is the only thing changing, and the Phase 3 architecture was built to make exactly this swap cheap. See [Phase 6](#phase-6--dedicated-server-with-multiple-rooms).

## Goals

- 4-player same-session play.
- Server-authoritative state; clients can't desync.
- Card effects are pure state machines, testable without Unity or Mirror.
- One composition root replaces the singleton zoo.
- **Player-facing card behavior is preserved exactly.** All 18 cards do what they do today.

## Non-goals (explicit)

- ~~Dedicated server / matchmaking infrastructure. **Host model only** for v1.~~ **Lifted 2026-08-21 — this is now [Phase 6](#phase-6--dedicated-server-with-multiple-rooms).** A self-hosted dedicated server with a room registry is the only way to get "ordinary people create a room and join it" without a VPN tool and without paying a third party. Matchmaking stays out of scope: room code, not skill-based matching.
- ~~Reconnect / disconnect handling. **If a player drops, behavior is undefined for v1.**~~ **Lifted — it is now a prerequisite, not a nice-to-have.** On the host model a dropped player was survivable because the host could just quit and take the process with them. On a 24/7 server a hung room leaks memory forever with nobody to restart it. **This is Phase 6.1 and it comes first.** Reconnect (a dropped player rejoining their seat) is still out of scope.
- Anti-cheat beyond what server-authority gives for free.
- New cards, new rules, new modes.
- Bots / AI players.
- Localization changes.

## Decisions locked in

- **Network model:** server-authoritative. ~~Listen-server (host is one of the players).~~ **Superseded 2026-08-21:** self-hosted **dedicated server** on the owner's own VM, holding several concurrent rooms in one process. Server authority itself is unchanged — only *where* the single engine instance lives.
- **Hosting:** the owner's own VM. Explicitly **not** a third-party relay (Edgegap, Unity Relay) and **not** Steam — both were considered and rejected as paid or account-gated. See [Playing over the internet](#playing-over-the-internet-how-the-hosting-decision-was-made).
- **Room addressing:** player creates a room by name, gets a short code, others join by code. No IP entry, no port forwarding, no VPN tool.
- **Migration shape:** incremental, one conceptual change per phase, every phase ships a playable build.
- **Effects:** rewrite internally; behavior parity verified against the Phase 0 checklist.
- **Transport:** keep Mirror. Keep KCP — with the server on a public IP, NAT is no longer a problem to solve.

---

## Progress (as of 2026-08-21)

- **Phase 0:** ✅ Done. Baseline tagged `pre-refactor-baseline`, [docs/CARDS.md](docs/CARDS.md) lists every card + observable effect + the A1/B2 rule effects.
- **Phase 1:** ✅ Functionally done. Every card effect and the rule-selection flow goes through `IDecisionRouter`. Dead code chain removed (`GameManager.PlayCard(Player)`, `Player.SelectCardInHand`, etc.). Verified in editor.
  - Carry-over: `IPlayerInput` is no longer *used* by core code, but the interface, `Player._playerInput`, and the adapter still live in `Assets/Libreries/`. They get deleted naturally in Phase 3 when `NetworkDecisionRouter` replaces `PlayerInputAdapterRouter`. Not blocking.
- **Phase 2.1:** ✅ Done. [GameSession.cs](Assets/Scripts/GameSession.cs) owns game state; `UnGameManager` is a host MonoBehaviour with forwarding properties for legacy callers. `GameNetworkController.TargetSetPlayer` calls `StartNewSession(...)` instead of poking four fields.
- **Phase 2.2:** ✅ UI components migrated. `BoardUI`, `PlayerHandUI`, `TextUIManager` no longer poll `UnGameManager.Instance` for readiness — they receive the session via `Initialize(GameSession)` from the composition root. Awaiting in-editor verification.
  - Carry-over (Phase 5): the lazy view-service singletons ([CardViewService.cs:19](Assets/Scripts/Services/CardViewService.cs#L19), [ItemVIewService.cs:17](Assets/Scripts/Services/ItemVIewService.cs#L17), [RuleEffectService.cs:24](Assets/Scripts/Services/RuleEffectService.cs#L24)) capture a null `gameManager` at first access during `Awake`. Latent issue, not introduced by this refactor.
- **Phase 3.0 (foundation):** ✅ Wire vocabulary defined as Mirror messages — [Assets/Scripts/Network/Messages/Intents.cs](Assets/Scripts/Network/Messages/Intents.cs) (`PlayCardIntent`, four `Resolve*Intent`s) and [Assets/Scripts/Network/Messages/DomainEvents.cs](Assets/Scripts/Network/Messages/DomainEvents.cs) (`GameStartedEvent`, the family of `Card*Event`s, `PointsAwardedEvent`, `TurnAdvancedEvent`, `DecisionRequestedEvent`, `GameEndedEvent`, etc., plus the `DecisionKind` enum).
- **Phase 3.1 (router):** ✅ [Assets/Scripts/Network/NetworkDecisionRouter.cs](Assets/Scripts/Network/NetworkDecisionRouter.cs) — server-only `IDecisionRouter` using the parked-TCS pattern. Each `PickX` parks a `TaskCompletionSource<T>` keyed by `RequestId`, broadcasts `DecisionRequestedEvent`, and awaits. The matching `Resolve*Intent` handler completes the TCS (validating the resolving connection matches the deciding player) and broadcasts `DecisionResolvedEvent`. Class is complete and ready to use; not wired into the game yet.
- **Phase 3.2 (client view + injection point):** ✅ Three pieces:
  - [Assets/Scripts/Network/ClientGameView.cs](Assets/Scripts/Network/ClientGameView.cs) — client-side mirror. Builds the full Card catalog at construction (same templates the server uses, same IDs), registers `NetworkClient` handlers for every `DomainEvent`, mutates a denormalized snapshot, and re-fires C# events with the *same names* the existing UI already subscribes to (`OnCardMovedToBoard`, `OnCardAddedToHand`, `OnAddPointsToPlayer`, etc.). Once UI swaps subscription source from `Session.Context.GameManager` to `ClientGameView`, the per-client engine can be deleted.
  - [GameBuilder.cs](Assets/Libreries/ScaryTales/GameBuilder.cs#L73) `Build(IDecisionRouter externalRouter = null)` — optional router parameter. Default behavior unchanged (still constructs `PlayerInputAdapterRouter`); the server-side `InitializeGame` will pass a `NetworkDecisionRouter` here.
  - `GameBuilder.MakeCardTemplates()` and `MakeItemTemplates()` exposed as `public static` so `ClientGameView` can build an identical Card catalog without instantiating a builder.
  - `GameStartedEvent.LocalPlayerId` field added — server sends this event per-client (not via `SendToAll`) so each recipient gets the same shared payload but with their own seat id, eliminating the need for a separate "you are player X" handshake.
- **Phase 3.3 (cutover):** ✅ Server-authoritative engine landed. End-state:
  - **[ServerEventBroadcaster.cs](Assets/Scripts/Network/ServerEventBroadcaster.cs)** subscribes to the canonical `GameSession.GameManager`'s C# events and broadcasts each as a wire `DomainEvent` via `NetworkServer.SendToAll`. One subscription per game manager event; one `SendToAll` per fire.
  - **[GameNetworkController.cs](Assets/Scripts/Network/GameNetworkController.cs)** is now the server-side composition root. `InitializeGame` builds a single canonical `GameSession` via `GameBuilder.Build(NetworkDecisionRouter)`, registers `NetworkServer` handlers for `PlayCardIntent` and `UseRuleEffectIntent`, sends per-client `GameStartedEvent`s with `LocalPlayerId` baked in, and starts an async server-side turn loop that draws, awaits the active player's `PlayCardIntent`, runs the effect, and advances. All Cmd*/Rpc* pairs and `TargetSetPlayer` deleted.
  - **[UnGameManager.cs](Assets/Scripts/UnityGameManager.cs)** is now purely client-side orchestration. `Awake` constructs `ClientGameView` so its `NetworkClient` handlers register before any DomainEvent arrives. `OnGameStarted` initializes the three UI components against the client view. `OnTurnAdvanced` enables drag for the local player on their turn (and starts a coroutine that sends `PlayCardIntent` on drop). `OnDecisionRequested` dispatches to per-kind prompt coroutines that show the existing card/item/rule UI and send back the matching `Resolve*Intent`. `ShowGameRules` (player-initiated rule use) sends `UseRuleEffectIntent`. `OnGameEnded` shows the result container. The host machine additionally caches the canonical session via `SetHostSession(...)` for host-side debug tooling.
  - **[BoardUI.cs](Assets/Scripts/UIEntities/BoardUI.cs), [PlayerHandUI.cs](Assets/Scripts/UIEntities/PlayerHandUI.cs), [TextUIManager.cs](Assets/Scripts/UIEntities/TextUIManager.cs)** `Initialize(...)` now takes `ClientGameView` instead of `GameSession`. Subscriptions point at the same-named events on the view (`OnCardMovedToBoard`, `OnCardAddedToHand`, `OnAddPointsToPlayer`, …) — the rename was a one-line subscription change per file plus type swap on the cached field.
  - **DragAndDrop / RuleContainer / ItemContainer** untouched in shape; `UnGameManager`'s coroutines now translate their existing event/click signals into `NetworkClient.Send(...intent...)` calls.
- **Phase 5.1 (IPlayerInput dead-chain cleanup):** ✅ Deleted: `IPlayerInput.cs`, `UnityPlayerInput.cs`, `PlayerInputAdapterRouter.cs`, `MockController.cs`, `INetworkController.cs`, `ConverterDTO.cs`, the commented-out `ConsolePlayerInput.cs`. Refactored: `Player.cs` (no `_playerInput`, single `(int id, string name)` constructor only), `GameBuilder.cs` (single `(notifier, board, p1, p2)` constructor; `Build(IDecisionRouter router)` now requires the router), `GameSession.cs` (no more `is PlayerInputAdapterRouter` branch), `NetworkPlayerInput.cs` (now an empty `NetworkBehaviour` shell — Mirror still spawns it as the per-connection player object), `GameConnectionManager.cs` (uses `Player(id, name)` directly), `GameNetworkController.cs` (orphan `PlayerDTO`/`NetworkPlayerRegistry` removed). Verified in editor.
- **Phase 5.2 (dead code + typos):** ✅ Deleted: `UnityGameBoard.cs`, `GameVisualManager.cs`, `PrincessEffect.cs`, `OgreEffect.cs`. Renamed: `ItemVIewService.cs` → `ItemViewService.cs`. Fixed typos: `AnumateCardTransformToPositionInLayout` → `AnimateCardTransformToPositionInLayout`, `ClearContentPanelchildren` → `ClearContentPanelChildren` (in `RuleContainer` and `ItemContainer`).
- **Phase 5.3 (factory cleanup):** ✅ Removed dead `IGameManager` param from [CardViewFactory.cs](Assets/Scripts/Factories/CardViewFactory.cs), [ItemViewFactory.cs](Assets/Scripts/Factories/ItemViewFactory.cs), [RuleEffectViewFactory.cs](Assets/Scripts/Factories/RuleEffectViewFactory.cs) and from [DragAndDrop.Initialize](Assets/Scripts/DragAndDrop.cs) — was stored but never read. Side effect: the previously-noted "view services capture a null `gameManager` at first access during `Awake`" latent bug is now obsolete (the services no longer capture `gameManager` at all).
- **Phase 5 remaining (deferred):** `Libreries`→`Libraries` folder rename (high-blast-radius; touches namespace/imports across the whole core library), `GameRulesConfig` ScriptableObject to replace hardcoded `new A1()`/`new B2()`, animation delays as serialized fields.
- **Phase 4.1 (N-player data model + lobby threshold):** ✅ Server, session, and client mirror are now player-count agnostic.
  - [GameConnectionManager.cs](Assets/Scripts/Network/GameConnectionManager.cs): `_targetPlayerCount` SerializeField (default 2). Auto-start fires when the roster fills; extra connections beyond the target are rejected.
  - [GameBuilder.cs](Assets/Libreries/ScaryTales/GameBuilder.cs): single constructor takes `IEnumerable<Player>`. No more 2-player hardcoding.
  - [GameSession.cs](Assets/Scripts/GameSession.cs): no more `LocalPlayer`/`LocalOpponent`. The canonical session is per-game, not per-seat. Exposes `Players` for callers that need the roster.
  - [ClientGameView.cs](Assets/Scripts/Network/ClientGameView.cs): `LocalOpponent` (singular) replaced by `Opponents` (`IReadOnlyList<Player>` of everyone except `LocalPlayer`, in seat order).
  - [PlayerHandUI.cs](Assets/Scripts/UIEntities/PlayerHandUI.cs) and [TextUIManager.cs](Assets/Scripts/UIEntities/TextUIManager.cs): `PlayerHandPanel3/4` and `Player3Name/Player3ScoreText` / `Player4Name/Player4ScoreText` SerializeFields added; iterate `_view.Opponents` and bind into whichever inspector slots are wired. Existing 2-player scenes continue to work without re-wiring.
  - [UnGameManager.cs](Assets/Scripts/UnityGameManager.cs) `LocalOpponent` forwarder now returns `ClientView.Opponents.FirstOrDefault()`.
  - [GameNetworkController.cs](Assets/Scripts/Network/GameNetworkController.cs): `InitializeGame` builds the session player-list-agnostic; the turn loop and `_playerConnections` validation already iterated `ctx.Players`.
- **Phase 4.2 (effects + IGameManager cleanup):** ✅
  - [EnchantedForestEffect.cs](Assets/Libreries/ScaryTales/CardEffects/EnchantedForestEffect.cs) rewritten to iterate `context.Players`. Active player still gets the "draw 1" plus, on Day, an extra one as part of "all players draw" — preserves the legacy 2-player behavior bit-for-bit. On Night, all players' card-discard picks fire concurrently via `Task.WhenAll(context.Router.PickCard(...))`, scaling to N. Players with empty hands are skipped instead of throwing.
  - `IGameManager.LocalPlayer`/`LocalOpponent` and the matching `GameManager` properties **deleted** — no consumers left after the EnchantedForest fix. Confirmed by grep.
  - `EnchantedForestCard` is still commented out in `GameBuilder.MakeCardTemplates()`. The effect compiles and is correct for any N; uncomment in `MakeCardTemplates` to add the card back to the deck.
- **Phase 4.3 (seat layout):** ✅ Done, code and scene. **Rewritten 2026-08-21** — the original circular model was replaced; see below.
  - New [Seat.cs](Assets/Scripts/UIEntities/Seat.cs) — data container bundling one player's UI: `HandPanel`, `NameText`, `ScoreText`, `BeforePlayerTable`. Fields are nullable so partial wiring works while iterating on layout.
  - [SeatLayout.cs](Assets/Scripts/UIEntities/SeatLayout.cs) — **explicit slot tables, not a formula.** A `SeatSlot` carries everything about one seat: `Position` (canvas px from the table centre), hand/table/label sizes and offsets, `FlipHand`, `HandBehindTable`, `FanAngle`, `FanVerticalOffset`, `CardScale`, `TableCardScale`. Three inspector arrays — `_slots2P` / `_slots3P` / `_slots4P` — one per player count; leave an array empty and `Apply(playerCount)` falls back to defaults hardcoded in `DefaultSlots(n)`. That fallback matters: Unity leaves a newly added array field null on a component serialized before the field existed.
  - `Apply(playerCount)` activates the first N seats and overwrites their transforms wholesale — whatever the scene holds is ignored on purpose, because the scene values had drifted (opponent seats were clones with name and score stacked on the same pixel; the local seat still carried pre-`Seat` absolute offsets like `(-807, -499)`).
  - **The seat root never rotates.** Only `HandPanel` does, by 180° for seats along the top edge. Rotating the whole seat is what threw the name/score labels to a different place on every seat.
  - Draw order is explicit (`ApplyDrawOrder` + `SetAsLastSibling`), because uGUI draws the last sibling on top and the scene happened to list `BeforePlayerTable` after `HandPanel`. Local seat: hand covers his own played cards. Opponents (`HandBehindTable = true`): the fan goes *behind* the played cards, so the two may overlap and the informative cards win the space.
  - [PlayerHandUI.cs](Assets/Scripts/UIEntities/PlayerHandUI.cs) / [TextUIManager.cs](Assets/Scripts/UIEntities/TextUIManager.cs) / [BoardUI.cs](Assets/Scripts/UIEntities/BoardUI.cs): `Initialize(view, seatLayout)`. Old `PlayerHandPanel1..4`, `Player1..4Name/ScoreText`, `OpponentTable`, `LocalPlayerTable` fields **deleted** — each component now reads its slots from the seats. `BoardUI` has a per-player `_beforePlayerTables` dict so each opponent's before-player cards land on their own seat's table instead of one shared `OpponentTable`.
  - [UnityGameManager.cs](Assets/Scripts/UnityGameManager.cs): `_seatLayout` SerializeField. `HandleGameStarted` calls `_seatLayout.Apply(ClientView.Players.Count)` then passes it to each UI's `Initialize`.
- **Phase 4.3 — the screen budget (why the numbers are what they are):** canvas is 1920×1080, origin at centre. Fixed furniture that seats must avoid: deck `x[-935,-785] y[155,405]`, time-of-day `x[-935,-785] y[-290,-40]`, discard `x[775,925] y[-290,-40]`, common table `x[-525,525] y[-69,181]`. That leaves the local player the known-good bottom bands (hand `y[-540,-320]`, his cards `y[-320,-70]`) and the opponents a **355px strip** along the top for labels + hand + played cards. A card is 250px tall, so opponent cards *must* shrink — currently 0.85 for played cards, 0.6 for the fan. There was never room for full-size opponent cards; that is why 3-player looked broken, not a bug in the layout code.
  - Seats sit at `y=-430` (local), and along the top at `y=456` with `x = ±450` for 3P (spans `[-760,-140]` and `[140,760]`, clear of deck and discard) and `x = -520/0/+520` for 4P.
  - **Verify layout arithmetically before running it.** The failed first attempt would have been caught by computing every zone rectangle and testing for overlap against the furniture list above. Two rounds of real bugs were found that way (opponent cards intersecting the common table; 4P name/score labels overwriting each other).
- **Phase 4.3 layout-related fixes in shared components:**
  - [CardTableLayout.cs](Assets/Scripts/UIEntities/Components/CardTableLayout.cs) — gained `scale` and `stackStep`; all math now runs on `cardSize * scale`, card `localRotation` is reset (cards arrive from a fan still carrying its rotation), and groups **compress inside the zone** instead of spilling out when they don't fit. Dead `shrinkThreshold` / `shrinkStep` removed — nothing read them.
  - [FanHandLayout.cs](Assets/Scripts/UIEntities/Components/FanHandLayout.cs) (`FanLayoutGroup`) — vertical placement now compensates for card scale. `SetChildAlongAxis` positions the *unscaled* rect and the card pivots at its bottom edge, so a scaled-down card kept its bottom and drifted out of the panel; that is why the scene had per-seat compensating `verticalOffset` values of −150 / 70 / −70. Now `verticalOffset == 0` means "fan centred in the panel" at any scale.
  - Scene: `GridLayoutGroup` swapped for `CardTableLayout` on all four `BeforePlayerTable` objects. Do **not** try to do this swap at runtime — Unity refuses `AddComponent` of a second `LayoutGroup` while the old one is alive, and `Destroy` only lands at end of frame, so `AddComponent` returns null. `SeatLayout.EnsureCardTable` keeps a `DestroyImmediate` self-heal path for seats wired before the swap.
  - Scene: legacy panels `PlayerHandPanelMain`, `PlayerHandPanelOpponent`, `LocalPlayerTable`, `OpponentTable` and the empty `TablePanel/SeatLayout` GameObject deactivated. No script referenced them, but each carried an `Image` and kept drawing over the table. `CommonTable` stays — `BoardUI.GameBoardPanel` points at it.
- **Phase 4.3 remaining:** visual polish only. The slot numbers are calculated, not eyeballed. Tune via the `SeatLayout` context menu: `Preview 2/3/4 players` applies instantly (works in Play Mode), `Fill defaults into inspector (2/3/4)` materializes the defaults into the serialized arrays so they can be nudged by hand.
- **Phase 4.4 (lobby UI):** ✅ Code-side done; pending scene work.
  - [GameConnectionManager.cs](Assets/Scripts/Network/GameConnectionManager.cs): renamed `_targetPlayerCount` to `_minPlayers` + `_maxPlayers`, exposes `Players` / `PlayerCount` / `CanStart` / `OnRosterChanged`. New `[Server] StartGameNow()` method does what `OnServerAddPlayer` used to do at full roster. Disconnect handling updates roster (mid-game disconnects are still v1-undefined per non-goals — they're logged but the game continues).
  - `_autoStartWhenFull` toggle (default `true`) preserves the legacy auto-start behavior so the game still works without a LobbyManager wired. Toggle off in your scene's GameConnectionManager once the lobby UI is in place — then the host controls the moment of game start.
  - New [LobbyManager.cs](Assets/Scripts/UIEntities/LobbyManager.cs) drives the UI. Detects host via `NetworkServer.active`. Subscribes to `OnRosterChanged` to refresh the player list, and to `ClientGameView.OnGameStarted` to dismiss itself when the game begins. Inspector slots: `_lobbyPanel` (root that gets disabled), `_statusText` (host-only "Players: 2/4 + names"), `_startButton` (host-only, only interactable when `CanStart`), `_waitingText` (non-host).
  - **Phase 4.4 scene work:** ✅ Done. `LobbyPanel` carries `LobbyManager` with `_lobbyPanel` / `_statusText` / `_startButton` / `_waitingText` all wired, and `GameConnectionManager` in the scene reads `_minPlayers: 2`, `_maxPlayers: 4`, `_autoStartWhenFull: 0` — so the host controls the moment of game start. Mid-game roster sync to non-host clients is intentionally out of scope — non-host clients see "waiting for host" until `GameStartedEvent` arrives.
- **Phase 4:** ✅ Done — see 4.1 through 4.4 above. (This line previously read "Not started", contradicting the four sub-entries.)
- **Phase 5:** partly done (5.1–5.3 above). Remaining deferred items are listed in "Phase 5 remaining" above.
- **Phase 6 (dedicated server + multiple rooms):** ⏳ In flight. Full plan in [Phase 6](#phase-6--dedicated-server-with-multiple-rooms).
- **Phase 6.2 (room-scoped delivery):** ✅ Code-complete, **not yet exercised in the editor**. `grep -rn "SendToAll" Assets/Scripts/` now matches only prose in comments — zero call sites. Compiles clean.
  - New [RoomChannel.cs](Assets/Scripts/Network/RoomChannel.cs) owns one room's seat→connection map *and* is its only outbound route: `SendToRoom`, `SendToSeat`, `IsSeatedAt`, `Bind`/`Unbind`. **Room-scoped by construction** — holding a channel gets you that room and nothing else, so a future send site cannot leak across rooms by forgetting to scope itself. It absorbed the `_connectionMap` that 6.1 introduced in `GameConnectionManager`. Phase 6.4's `Room` is this plus the session, router, broadcaster and turn loop.
  - Call sites converted: `ServerEventBroadcaster` 11, `GameNetworkController` 3 (+ the `GameStartedEvent` fan-out, now `SendToSeat`), `NetworkDecisionRouter` 2, `GameConnectionManager` 1 (`LobbyStateUpdate`). The plan predicted 22; it was 17 by the time this ran, because 6.1's `Ask<T>` collapse had already removed three from the router.
  - `IsSeatedAt(seatId, conn)` replaced three hand-rolled `TryGetValue(...) || expected != conn` checks (both intent handlers plus `TryClaim`). It is the authorization primitive behind "is this intent really from who it claims to be" — worth a name, because getting it subtly wrong is how a client plays out of turn.
  - **Known, accepted cost:** `SendToRoom` packs the message once *per recipient*; `NetworkServer.SendToAll` packed once and reused the segment. Its fast path is `NetworkConnection.Send(ArraySegment<byte>, int)`, which is `internal` and unreachable from `Assembly-CSharp`. At 2-4 seats with id-sized messages this does not matter — and it is not a reason to reach back for `SendToAll`. Recorded in the method's own comment.
  - **Behavior delta worth knowing:** sends now reach *seated* connections rather than *all connected* ones. Checked the three places that differ — a client between connect and `OnServerAddPlayer` (Mirror seats them immediately, and no game events fire in that window), a seat unbound mid-game (the leaver correctly does **not** receive their own room's abort, since `OnServerDisconnect` unbinds before `OnSeatVacated` runs), and a connection rejected at the door (which is disconnected anyway, so no longer getting lobby state is an improvement). No regression.
  - Reentrancy checked: `SendToRoom` iterates the seat dictionary without snapshotting, same as Mirror's own `SendToAll`. Safe because `LocalConnectionToClient.Send` enqueues for the next update rather than invoking handlers synchronously, so a host-side handler cannot mutate the dictionary mid-iteration.
  - **New file needs its `.meta`:** `RoomChannel.cs` was created outside the editor, so Unity generates `RoomChannel.cs.meta` on its next import. Commit both. (No GUID risk — it is a plain C# class, referenced by no scene or prefab.)
- **Phase 6.3 (one handler set, dispatched by room):** ✅ Code-complete, **not yet exercised in the editor**. `grep -rn "NetworkServer.RegisterHandler" Assets/Scripts/` returns six lines, all in one method — none per game. Compiles clean.
  - New [ServerIntentDispatcher.cs](Assets/Scripts/Network/ServerIntentDispatcher.cs) claims all six intent types once, keeps a `connectionId → room` index, and dispatches. Authorization stays in the room (`RoomChannel.IsSeatedAt`) — this layer answers "which room?", not "may you?".
  - `GameNetworkController.InitializeGame` and the `NetworkDecisionRouter` constructor no longer register anything. The router's four `OnResolve*` became public entry points; the controller's two became `HandlePlayCard` / `HandleUseRuleEffect`, reached via a new `Router` property for the resolve path.
  - **6.1's handler-unregistration is now deliberately reversed.** `NetworkDecisionRouter.Dispose` and `GameNetworkController.Teardown` used to pull their handlers down so a finished game wouldn't intercept the next one's intents. That was right when registration was per-game; under 6.3 the handlers are shared, and a finished room tearing them down would silence *every other room*. Retiring a room is now purely an index operation: `Teardown` raises a new `RoomFinished` event, `GameConnectionManager.OnRoomFinished` calls `ServerIntentDispatcher.UnbindRoom`.
  - **Ordering trap found and closed.** `OnStartServer` looks like the obvious place to register, but Mirror does not guarantee it runs before clients arrive: `SetupServer()` calls `NetworkServer.Listen()` and only `FinishStartHost()` later calls `OnStartServer()` — with an **async scene load in between when `onlineScene` is set**. Mirror's own source flags the gap (`// TODO is there a risk of someone connecting between Listen() and FinishStartHost()?`, [NetworkManager.cs:553](Assets/Mirror/Core/NetworkManager.cs#L553)). Binding a room against a null dispatcher would have delivered no intents at all **with nothing in the log**. Fixed with an idempotent `EnsureDispatcher()` called from both `OnStartServer` and `StartGameNow`. Reachable today only with `_autoStartWhenFull` on (the scene has it off), but it is exactly the kind of silence that costs a day later.
  - The dispatcher is dropped (not cleared) in `ResetRoom`, because `NetworkServer.Shutdown()` clears the handler table immediately after `OnStopServer` — so the old delegates go with it and the next server start builds a fresh dispatcher with an empty index.
  - **What cannot be verified yet, and why:** the plan's own risk note says to "test with two rooms from the very first commit of 6.3". That is not possible before 6.4 — there is no second room to make. 6.3 is therefore *structurally* correct but its payoff is unproven until the registry lands. What a single-room editor run **does** prove is that every intent still arrives after taking an entirely new route, which is a real regression test: card play, rule use, and all four decision kinds now reach the session through the dispatcher rather than direct registration.
  - `ServerIntentDispatcher.cs` also needs its generated `.meta` committed alongside it.
- **Phase 6.4a (Room extraction):** ✅ Code-complete, **not yet exercised in the editor**. Compiles clean. No behavior change: still one room, still joined by IP.
  - `GameNetworkController.cs` → **[Room.cs](Assets/Scripts/Network/Room.cs)** (`git mv`, history preserved). It absorbed the roster, seat map, `_nextSeatId`, `_gameStarted`, lobby broadcast and the departure policy that were process-global fields on `GameConnectionManager`. Everything one game needs now hangs off one object — which is the property Phase 6 is actually buying.
  - **No longer a `NetworkBehaviour`.** Phase 3.3 had already deleted every `Cmd`/`Rpc`, leaving a networked object with no networked behaviour. Gone with it: the prefab instantiate, `NetworkServer.Spawn`, the `[Server]` attributes, and the `gameNetworkControllerPrefab` SerializeField. The rule-id SerializeFields moved to `GameConnectionManager` (server config) and are passed to the `Room` constructor.
  - `GameConnectionManager` is now address translation and lifecycle only: Mirror callbacks → room operations, plus the dispatcher and the registry. Its lobby-facing surface (`Players`, `PlayerCount`, `MaxPlayers`, `CanStart`, `OnRosterChanged`, `StartGameNow`) is unchanged, so **`LobbyManager` needed no edits at all**.
  - **Still one room on purpose.** The registry is a single field, not a dictionary — nothing can ask for a second one until 6.4b. The seam is `EnsureRoom()` / `RoomFor(conn)`; everything downstream of those two is already per-room.
  - `ServerIntentDispatcher` now indexes `Room`, and binds **at join** rather than at game start — the room exists from lobby time now, so the old "nothing can arrive before StartGameNow" assumption is no longer needed at all.
  - **Regression caught while writing this:** the old controller's `OnStopServer()` aborted the game when the server stopped. A plain C# object gets no such callback, so stopping the server mid-game would have left parked decisions uncancelled and skipped the notice to clients. `GameConnectionManager.ResetServer()` now calls `_room.AbortGame(0, "Сервер остановлен.")` explicitly. Losing a Unity lifecycle hook is the standard cost of de-MonoBehaviour-ing something; the fix is to make the new owner call it.
  - **Left for 6.4b:** room codes and code generation, the create/join/leave wire messages and their failure cases, and destroying a room when it empties. Today a finished room lingers until `OnStopServer` — harmless on the host model, unacceptable on a 24/7 server.
  - **The orphaned prefab was not cosmetic — it was a hard error.** `git mv` carried the old `.meta` across, so `Room.cs` kept `GameNetworkController.cs`'s GUID, and `Assets/Resources/GameNetworkController.prefab` still had a MonoBehaviour component pointing at it. Unity does not degrade that to a "missing script" warning; it throws **`'Assets.Scripts.Network.Room' is missing the class attribute 'ExtensionOfNativeClass'!`** — the guid resolves, but to a class that is not native-backed. Fixed by deleting the prefab and its `.meta` (`git rm`), which was the last asset referencing that GUID.
    - Safe to delete outright: Mirror filters the list with `spawnPrefabs.Where(t => t != null)` ([NetworkManager.cs:769](Assets/Mirror/Core/NetworkManager.cs#L769)), so the now-dangling scene entries resolve to null and are skipped silently.
    - **Lesson for the next case-of-this:** when a `git mv` turns a MonoBehaviour into a plain class, the carried-over `.meta` keeps every prefab and scene reference pointing at the renamed file. Either give the new file a fresh GUID or delete the referencing assets — checking that "nothing in code references it" is not enough, because the reference lives in YAML.
  - **Still left for the owner in the editor (genuinely cosmetic):** both scenes keep a null entry in `spawnPrefabs` and a stale `gameNetworkControllerPrefab:` line. Mirror skips the former; the latter drops itself on the next scene save, since the field no longer exists. Tidy whenever convenient.
- **Phase 6.4b + 6.6 (room codes, join-by-code, client):** ✅ Code-complete, **not yet exercised in the editor**. Compiles clean.
  - **Connecting is no longer joining.** A connection now arrives in the lobby with no room and no seat, and gets one only by sending `CreateRoomIntent` or `JoinRoomIntent`. `OnServerAddPlayer` no longer seats anybody — that override is gone entirely. This is the change that lets one process hold several games.
  - New [RoomRegistry.cs](Assets/Scripts/Network/RoomRegistry.cs): `code → Room` (case-insensitive) plus `connectionId → Room`. The connection index **moved out of `ServerIntentDispatcher` into the registry** so there is one answer to "where is this player" and no chance of the routing table and the room list drifting apart. The dispatcher now takes the registry and lost its own Bind/Unbind/Clear surface.
  - **Room codes: letters only, no I/L/O, four characters** (23⁴ ≈ 280k). Letters only because the code's whole job is to be read aloud to a friend, and mixing digits in imports the S/5, Z/2, B/8 family of mishearings; I, L and O go because they are the ones people retype as 1 and 0 even when reading off a screen. `Normalize` uppercases and strips spaces/dashes but deliberately **does not** strip out-of-alphabet characters — an unmatched code is a truer answer than silently deleting what the player typed and landing them in some other room.
  - New wire messages in [LobbyMessages.cs](Assets/Scripts/Network/Messages/LobbyMessages.cs): `CreateRoomIntent`, `JoinRoomIntent`, `LeaveRoomIntent`, `StartGameIntent`, `RoomJoinedEvent`, `RoomJoinFailedEvent` (+ `RoomJoinFailure` enum). `LobbyStateUpdate` gained `CanStart`, sent rather than recomputed client-side so the min-player rule lives in one place. Room-lifecycle handlers are registered by `GameConnectionManager`, not the dispatcher — they arrive from connections that are in no room, so there is nothing to dispatch *to*; same "once per server" rule applies.
  - **`StartGameIntent` replaces the host's direct call.** The room's creator is the only one obeyed, checked against `OwnerSeatId` — a seat, not a connection, for the same reason `Player.Id` is (6.1). On a dedicated server the creator is an ordinary client with no access to the room object, so the moment of starting has to travel over the wire.
  - **`LobbyManager` is now entirely client-side.** It used to read the server's roster directly whenever `NetworkServer.active` said "you are the host", which stops meaning anything once the server is its own process. Both facts it needs arrive over the wire. Its SerializeField names are unchanged, so **no scene re-wiring is needed**.
  - `MenuManager`: server address is a serialized field (default `localhost`), the existing text input is now room name (create) or code (join), and both buttons connect-then-ask via a coroutine with a timeout. Its `roomNameInput` field name was kept deliberately so the existing wiring survives. New optional `statusText` slot reports connect and join failures; null-safe if left unwired.
  - **Rooms are destroyed when abandoned** — `Channel.Count == 0`, i.e. no live connections. Deliberately not "empty roster": a mid-game departure keeps its seat by design (6.1), so the roster can be non-empty while nobody is there. This is what keeps a long-lived server's memory flat.
  - `Room.RosterChanged` and `Room.Finished` were added in 6.4a and **removed again here** — with the lobby client-side and the room broadcasting its own state, nothing listened to either.
  - **The two-room test still is not possible, and this is why:** on the host model "create a room" is `StartHost`, so a second room would need a second client to create one — which is only natural once everybody is a plain client of a shared server. That is 6.5. What 6.4b *does* make testable now: codes, join-by-code, every refusal path, owner-only start, and room destruction.
  - `RoomRegistry.cs` needs its generated `.meta` committed alongside it.
  - **Fixed room code for local testing.** `GameConnectionManager._fixedRoomCode` (default `LOCALHOST`) gives the first room that code instead of a random one; `MenuManager.fallbackRoomCode` (same value) is what Join uses when the input is left empty. Together they mean neither side has to type or read a code during a normal test run. **Both are switched off by clearing the field** — random codes and a required typed code come back with no other change, so this is a toggle rather than a thing to undo later. A second room created while the fixed code is taken falls back to a random one, so it does not block the two-room test. Note the fixed code is deliberately allowed to be a word rather than four letters: only *generated* codes have to obey the alphabet, and `Normalize` never strips out-of-alphabet characters.
- **Phase 6.5 (dedicated server entry point):** ✅ Code-complete, **not yet run as a server**. Compiles clean.
  - `GameConnectionManager.Start()` overrides Mirror's, calls `base.Start()` first (so a scene-configured `headlessStartMode` still wins), and starts a dedicated server if nothing else claimed the process.
  - **Two ways in, and the second one is the point.** Mirror's `Utils.IsHeadless()` is `graphicsDeviceType == Null` — true only for a Dedicated Server build target or `-batchmode -nographics`. An ordinary windowed build never satisfies it, so relying on `headlessStartMode` alone would mean building a separate server target before anything could be tested. A **`-server` command-line flag** lets the normal build act as one. There is also `-port <n>`, applied through `Transport.active as PortTransport`, for running a server beside an editor on one machine.
  - `IsDedicatedServer` (static) is now the honest test for "this process runs the engine for other people and plays no part". It gates two things the host model had conflated: `ReturnToMenu` is a no-op on a server (it has no menu, and one room ending must not tear down the others — room cleanup is `ResetServer`'s job, already split out), and `Room.StartGame` no longer hands the local `UnGameManager` a session, which on a server would mean whichever room started last.
  - Canvases are switched off on a dedicated server (`_hideUiOnDedicatedServer`). The UI is already inert there — it is driven by `ClientGameView`, fed by a `NetworkClient` that never connects — so this is about not spending frames on layout, not correctness. `Application.targetFrameRate = sendRate` for the same reason: Mirror caps it only when it detects headless, so a `-server` build would otherwise render nothing as fast as it could.
  - **`autoCreatePlayer` left alone on purpose.** The plan suggested turning it off since `playerPrefab` is the empty `NetworkPlayerInput` shell. It costs one inert spawned object per connection and turning it off is a scene setting with non-obvious Mirror side effects (`conn.identity`, readiness). Not worth the risk for the gain; flip it in the inspector if it ever matters.
  - **Client-side twin of the 6.3 bug, caught before shipping.** `MenuManager` needed to report join outcomes (its status slot is optional and was unwired, so a refused click looked like a dead button) — but registering `RoomJoinedEvent` there would have silently replaced `LobbyManager`'s handler for the same type. Mirror keeps one handler per message type on the **client** exactly as on the server. New [RoomClient.cs](Assets/Scripts/Network/RoomClient.cs) claims the three room-lifecycle messages once and fans them out as C# events. It uses `ReplaceHandler` and is safe to call from every subscriber's `Awake`: a register-once guard would be wrong, because `NetworkClient.Shutdown()` clears the whole handler table on every `StopClient()`, so the client would go deaf after the first trip back to the menu.
  - **Stuck-in-a-room fixed.** A connection that had joined a room could never create or join another: both answered `AlreadyInRoom` and there is no Leave button. `TryLeaveCurrentRoom` now moves a connection out of a room whose game has not started, since pressing the button again plainly means that; once their game is running they are committed and the request is still refused.
  - **"Create Room does nothing on the first click" — root cause found in a build log, 2026-08-30.** With `hostLocallyOnCreate` still `true`, Create called `StartHost()`, which binds the transport port; the dedicated server already held it, so `kcp2k` threw `SocketException` **out of the click handler**, before `StartCoroutine` was ever reached. No intent sent, no coroutine, nothing on screen — only a stack trace in a log nobody was watching. Pressing Join then worked (`StartClient`), after which Create worked too, because `NetworkClient.isConnected` short-circuits past `StartHost`. That is exactly the sequence the owner reported.
    - Fixed by checking the port *before* hosting (`IsListenPortFree`) and falling back to `StartClient` — which is the right answer, not a consolation: something is already serving on that address. The check comes first rather than a catch afterwards because a failed `StartHost` leaves Mirror half-initialized, and `StopHost()` from there re-enters `OnClientDisconnect` → `ReturnToMenu`. Any remaining throw is caught and reported instead of killing the click.
    - **Lesson:** a UI button whose handler can throw is a button that silently does nothing. Every entry point that starts networking needs to report its own failure.
  - **The two-room test — ✅ PASSED 2026-08-30.** Server log:
    ```
    [Server] Room 'localhost' created with code LOCALHOST (1 live).
    [Server] Player1 took seat 1 in room LOCALHOST: 1/4
    [Server] Player2 took seat 2 in room LOCALHOST: 2/4
    [Server] Room 'localhost' created with code VUXM (2 live).
    [Server] Player1 took seat 1 in room VUXM: 1/4
    [Server] Player2 took seat 2 in room VUXM: 2/4
    [Room] Starting game with 2 players.
    [Room] Starting game with 2 players.
    ```
    One process, two rooms, two players each, both games running at once, both receiving their own players' intents. **This is the exit criterion 6.2 and 6.3 were written against and could not be checked until now** — 6.3's failure mode (a second room's handler registration silently deafening the first) is invisible with a single room, and both rooms stayed responsive.
    - The first attempt that day did *not* count: `hostLocallyOnCreate` was still `true`, so each client was its own server — two processes with one room each. It is a serialized field, so it had to be unticked in the scene and rebuilt; changing the initializer in code would not have helped.
    - **Not yet observed, and still open:** rooms being freed when abandoned (`[Server] Room ... abandoned; destroying` never appeared — the run ended before anyone left), and a mid-game disconnect not disturbing the *other* room (6.1 was only ever verified with one room). Both are cheap to check on the next multi-room run.
    - Cosmetic: both rooms are named `localhost`, because the text box still held the old IP-entry value. The field is the room *name* now.
  - **Second multi-room run, 2026-08-30: both remaining behaviours confirmed.** One room played to a winner and everyone returned to the menu; in the other a player quit mid-game and the survivor got `[Client] Game aborted (left: Player2)` and returned to the menu — without disturbing the first room.
  - **"Cannot start a second game without restarting the client" — found and fixed.** After `ReturnToMenu` reloads the scene, both Create and Join threw:
    ```
    Multiple NetworkManagers detected in the scene. ... The duplicate NetworkManager will be destroyed.
    NullReferenceException at Mirror.NetworkManager.InitializeSingleton()
      at Mirror.NetworkManager.StartClient() / MenuManager.TryStartNetwork
    ```
    Mirror's `NetworkManager` is `DontDestroyOnLoad` and **reparents itself to the scene root** to make that work ([NetworkManager.cs:713](Assets/Mirror/Core/NetworkManager.cs#L713)), so it survives the reload. The reloaded scene brings its own, Mirror destroys that one as a duplicate — and `MenuManager`'s **serialized** `networkManager` field pointed at exactly that dead duplicate. Fixed by going through `NetworkManager.singleton`, keeping the serialized field only as a first-frame fallback.
    - Pre-existing, not introduced by Phase 6: the old `MenuManager` used the same serialized reference. It only became reachable once games actually ended and returned to a menu you could start from again — which is to say, once the product worked.
    - **Lesson:** a serialized reference to a `DontDestroyOnLoad` singleton is a reference to whichever copy the *scene* holds, not the one that is alive. Resolve such singletons at use time.
  - **Noticed, not fixed:** `AnimationManager` and `CursorManager` both call `DontDestroyOnLoad` on child GameObjects, which does nothing (Unity logs the warning; unlike Mirror, they do not reparent to root). They therefore do *not* survive scene reloads. Worth confirming that is intended.
  - **Room freeing confirmed 2026-08-30.** After a three-player room emptied, the next `[Server] Room 'localhost' created with code LOCALHOST` reported **`(1 live)`** — the previous room had been destroyed, not accumulated. That was the last unobserved Phase 6 exit criterion.
  - **Second game in the same process was unplayable — the real cost of Phase 2's unfinished item.** Cards appeared loose in the scene rather than in a hand, and every card was draggable. Three plain-C# statics survive the scene reload that ends a game: `CardViewService`, `ItemViewService`, `RuleEffectService`. Being plain classes rather than MonoBehaviours, **Unity's fake-null never applies to them** — the stale instance kept a `CardViewFactory` built around the destroyed scene's `GameBoardPanel`, and instantiating against a destroyed parent yields *no* parent, so cards landed at the scene root. Two more statics compounded it: `DragAndDrop.SelectCard` left `true` by a game that ended mid-selection, and `CardSelectionService.CurrentSelectionHandler` still pointing into a finished game's coroutine.
    - Fixed with a `Reset()` on each service, called from `UnGameManager.Awake` — once per scene load, which is exactly the lifetime these should have had. Phase 2 listed "singleton view services become session-scoped, built fresh per game, no leak across sessions"; it was never done, and this is the bill.
    - **Lesson:** in Unity, a static holding a MonoBehaviour is *usually* saved by fake-null; a static holding a plain C# object that in turn holds scene references is not. The second kind needs an explicit lifetime.
  - **Noticed in the same log, not fixed (scene work):** `DontDestroyOnLoad only works for root GameObjects` — the `NetworkManager` is a child object, so it does *not* survive scene reloads as Mirror intends. Harmless in the current flow (`ReturnToMenu` reloads everything anyway) but worth making a root object.
  - **The two-room test — finally runnable, and still outstanding.** It is the exit criterion 6.2 and 6.3 have been waiting on since they were written. Recipe: build normally; run `ScaryTales.exe -server`; set `MenuManager.hostLocallyOnCreate = false` and `serverAddress = localhost`; then run four clients (editor, ParrelSync clones, extra copies of the build). Two create rooms, two join — the first room gets `LOCALHOST` from the fixed-code setting, the second gets a random code its creator reads off the lobby screen. **What to watch for:** neither room seeing the other's cards, turns, decisions or scores (that is 6.2), and both rooms still receiving their players' intents rather than one going silent (that is 6.3 — the failure mode it exists to prevent is invisible with a single room).
  - **Fixed in first testing (`LobbyManager`):** the room code and roster rendered on top of themselves. Two causes, both mine — the `_statusText.gameObject.SetActive(...)` line was lost in the rewrite (the two texts share a spot in the panel and had always been mutually exclusive), and the same header+roster string was being written into both texts. Now exactly one is active and only that one is written to.
- **Phase 6.1 (disconnect handling):** ✅ Code-complete, **pending in-editor verification** (see the test matrix below). Compiles clean via `dotnet build Assembly-CSharp.csproj`; Mirror's weaver has *not* been run yet, because the editor was open — it processes `[Server]` and generates the serializer for the new `GameAbortedEvent`, so the editor's own recompile is the real gate.
  - **Policy decided: a mid-game departure ends the room.** The alternative (carry on a player short) needs the engine to drop somebody from the turn order mid-game — a change to `Assets/Libreries` plus a re-audit of all 18 effects. Not worth it to unwedge a room. Recorded in [GameConnectionManager.OnSeatVacated](Assets/Scripts/Network/GameConnectionManager.cs), which is the single place the policy lives.
  - **Seat id is no longer the connection id.** `Player.Id` used to be `conn.connectionId`; it is now a monotonic seat id (starting at 1 — **0 is the "no player" wire sentinel** and must stay free). `_connectionMap` (seat → connection) is the one mutable binding, with `_seatByConnection` as the reverse index for `OnServerDisconnect`. This was confined to `GameConnectionManager` — nothing else conflated the two, confirmed by grep for `connectionId` under `Assets/Scripts/`. **This is the prerequisite the parking lot names for reconnect:** a returning player gets a new connection id, and rebinding `_connectionMap[seatId]` is how they reclaim their seat.
  - **Parked decisions are cancellable.** [NetworkDecisionRouter.cs](Assets/Scripts/Network/NetworkDecisionRouter.cs) now parks a `PendingDecision` (player + the verbatim `DecisionRequestedEvent` + a type-erased `Fail` closure) instead of two parallel dictionaries. `CancelForPlayer` / `CancelAll` fault each TCS with a new `DecisionAbandonedException : OperationCanceledException`, which unwinds the suspended effect. The request is kept verbatim on purpose — **re-sending it to a returning connection is exactly what reconnect needs**, and storing it costs nothing. `Dispose()` also unregisters the four `Resolve*Intent` handlers, since Mirror keeps one handler per type process-wide (the 6.3 problem, in miniature).
  - **The four `PickX` methods collapsed into one `Ask<T>` helper** — they differed only in resolution type and candidate-id namespace.
  - **Nothing in `Assets/Libreries` catches anything** (verified by grep), so a cancellation propagates cleanly from inside an effect all the way to the server turn loop. That is what makes fault-the-TCS viable without touching the core.
  - **The turn loop cancels even when it isn't awaiting.** Non-obvious and the one real bug found while writing this: cancelling parked TCSs only helps if the loop is *suspended*. An abort landing while it was running (dealing the opening hands, or between statements) had nothing to cancel — the loop would sail on, park on a fresh `_waitingForPlay`, and wedge exactly as before. `ThrowIfGameOver()` after every await point closes it, funnelling every abort onto the single `OperationCanceledException` path.
  - `_gameOver` (both endings) is separate from `_aborted` (early ending only). `_gameOver` makes every teardown path idempotent, and stops a later `OnStopServer` from overwriting the winner screen with a teardown notice.
  - **`GameNetworkController.Instance` deleted** — the dead static the plan flagged in 6.4. Its `Awake` did `Destroy(gameObject)` on a second instance, which would have been an active landmine the moment a second room existed.
  - New `GameAbortedEvent { LeftPlayerId, Reason }`, deliberately *not* a `GameEndedEvent` with a synthetic winner: an aborted game has no winner and clients must not render a podium for one. `ClientGameView.OnGameAborted` → `UnGameManager.HandleGameAborted` stops the prompt coroutines, shows the reason via new `ResultContainer.ShowMessage`, and returns to the menu.
  - **`ReturnToMenu` re-entrancy fixed.** `ReturnToMenu → StopHost → StopClient → OnClientDisconnect → ReturnToMenu` is a real cycle in Mirror's teardown; a static guard makes the first call win. It is cleared from `SceneManager.sceneLoaded`, not a `finally`, because `LoadScene` completes at end of frame. The paired null-`conn` crash is fixed too: `StopClient()` passes `NetworkServer.localConnection` to `OnServerDisconnect` in host mode ([NetworkManager.cs:638-639](Assets/Mirror/Core/NetworkManager.cs#L638-L639)) and it can already be null.
  - **Room state resets on `OnStopServer`.** `NetworkManager` is `DontDestroyOnLoad`, so it survives the scene reload — without this the next `StartHost` came up with `_gameStarted` still true and rejected every join. `ResetRoom` allocates fresh collections rather than clearing shared ones, and is split from "show the menu" as 6.5 wants.
  - **Left for 6.4 on purpose:** `ServerEventBroadcaster` still never unsubscribes. Harmless today (the whole object graph becomes unreachable together), but it needs a `Dispose` once rooms are recycled in a long-lived process.
  - **Verified in the editor and committed as `aea36e3`** (code only — this plan and `docs/` remain untracked by the owner's choice). Confirmed: a player leaving mid-game shows the others "Player 3 покинул игру" and ends the game.
  - **Verification matrix (do this in the editor with the two ParrelSync clones):** (1) joiner quits mid-game → host and the other client both see "…покинул игру", then the menu; (2) joiner quits *while the server is waiting on their decision* → same, and the log shows the abandoned request — this is the wedge case, so test it deliberately; (3) host quits mid-game → clients land on the menu with no NRE in the log; (4) play a game to its natural end → winner screen, **not** the abort text; (5) after any of the above, start a fresh game from the same editor session → joins are accepted (this is the `ResetRoom` check).

---

## Phase 0 — Pin the baseline

**Goal:** establish "what works today" so we can verify behavior parity later.

- [ ] Tag current commit as `pre-refactor-baseline`.
- [ ] Fill in the [behavior parity checklist](#behavior-parity-checklist) below — one observable-behavior line per card.
- [ ] Record a short video or screenshot sequence of a happy-path 2-player game (optional but cheap insurance).
- [ ] List dead-code targets so we don't lose them: [UnityGameBoard.cs](Assets/Scripts/UnityGameBoard.cs), [GameVisualManager.cs](Assets/Scripts/GameVisualManager.cs), unused [INetworkController.cs](Assets/Scripts/Network/INetworkController.cs).

**Exit criteria:** behavior checklist filled, baseline tag pushed.

**Code changes:** none.

---

## Phase 1 — Decision Request pattern (no network changes yet)

**Goal:** stop effects from awaiting input directly. They produce `DecisionRequest`s; a router resolves them.

### What gets built

- `Assets/Libreries/ScaryTales/Decisions/` — new namespace:
  - `DecisionRequest` (abstract or sealed hierarchy): `PickCardFromBoard`, `PickCardFromHand`, `PickItem`, `PickRuleEffect`, `PickPlayer`, `Confirm`. Each carries the candidate IDs.
  - `DecisionResolution` (sealed per request): `CardPick(int cardId)`, `ItemPick(int itemType)`, `PlayerPick(int playerId)`, etc.
  - `IDecisionRouter`: `Task<DecisionResolution> Decide(int playerId, DecisionRequest request, CancellationToken ct)`.
- One concrete router for now: `LocalDecisionRouter`. Routes to existing UI for the local player; for remote players, it forwards through the existing Mirror Cmd/Rpc pair (preserves Phase-0 network behavior).
- Effects rewritten one at a time. Mechanical translation:
  ```csharp
  // Before:
  var place = await player.SelectCardAmongOthers(places);

  // After:
  var pick = (CardPick)await ctx.Router.Decide(
      player.Id,
      new PickCardFromBoard(places.Select(c => c.Id).ToList()),
      ct);
  var place = ctx.GameBoard.GetCardById(pick.CardId);
  ```
- Delete `IPlayerInput` from core, [UnityPlayerInput](Assets/Scripts/Network/), [NetworkPlayerInput.cs](Assets/Scripts/Network/NetworkPlayerInput.cs). The TCS pattern moves into `LocalDecisionRouter`'s remote path (still TCS, still awful, but localized — Phase 3 kills it).

### What does NOT change

- Card behavior (verified against Phase 0 checklist).
- Networking model (still 2P, still everyone-replays).
- UI (still talks to `UnGameManager.Instance`).

### Exit criteria

- All 18 effects rewritten and verified against Phase 0 checklist.
- `IPlayerInput` removed from core. Grep `IPlayerInput` returns zero results in `Assets/Libreries/`.
- 2-player happy path works end-to-end.

### Risks

- 18 cards × manual verification. Mitigation: do 2-3 at a time, run in editor between batches.
- Iterator-vs-async-vs-state-machine choice for effect suspension. Recommendation: plain `async Task` with `IDecisionRouter` — same control flow as today, minimal mental shift.

---

## Phase 2 — Composition root, kill singletons

**Goal:** one `GameSession` owns the world. `UnGameManager.Instance` and friends go away.

### What gets built

- `GameSession` class: holds `IGameContext`, `IDecisionRouter`, references to UI bindings, lifecycle (`Start`, `EndGame`, `Dispose`). Built once per game in a single composition root method.
- `UnGameManager` shrinks to a thin MonoBehaviour that hosts the session and forwards Unity lifecycle (Awake, Update). All game-logic methods move into `GameSession`.
- Replace ~43 `UnGameManager.Instance.X` references:
  - In MonoBehaviours: get the session via `[SerializeField]` reference or constructor (if pure C#).
  - In `GameNetworkController`: receive session via `Init(session)`.
  - In services / factories: take session as a constructor argument.
- Singleton view services ([CardViewService.cs](Assets/Scripts/Services/CardViewService.cs), [ItemVIewService.cs](Assets/Scripts/Services/ItemVIewService.cs), [RuleEffectService.cs](Assets/Scripts/Services/RuleEffectService.cs)) become session-scoped. Built fresh per game; no leak across sessions.
- Static [CardSelectionService.cs](Assets/Scripts/Utilities/CardSelectionService.cs) → instance method on session or input adapter.

### What does NOT change

- Network model, card behavior, UI rendering.

### Exit criteria

- Grep `\.Instance` in `Assets/Scripts/` shows only Mirror's own singletons (NetworkServer, NetworkClient).
- New game can be started, ended, started again with no stale state.
- 2-player happy path still works.

### Risks

- MonoBehaviour ↔ pure C# wiring gets fiddly. No DI container needed; one composition root method (`GameSession.Build(...)`) is enough.

---

## Phase 3 — Server-authoritative engine

**Goal:** only the host runs the engine. Clients send intents, receive events, render.

> Done. Read "the host" as "the single server process" — Phase 6 moves that process off a player's machine onto our own VM, which is precisely the swap this phase made cheap.

### Wire protocol (final shape)

**Client → Server (intents):**
- `PlayCardIntent { int CardId }`
- `ResolveDecisionIntent { Guid RequestId, DecisionResolution Resolution }`
- `StartGameIntent` (host only, signals lobby exit)

**Server → Clients (domain events):**
- `GameStarted { PlayerInfo[] Players, int[] DeckOrder }`
- `CardDrawn { int PlayerId, int CardId }`
- `CardPlayed { int CardId, int PlayerId }`
- `CardMoved { int CardId, CardPosition From, CardPosition To }`
- `PointsAwarded { int PlayerId, int Delta }`
- `TurnAdvanced { int CurrentPlayerId, int TurnCount }`
- `DecisionRequested { Guid RequestId, int PlayerId, DecisionRequest Request }`
- `RuleEffectApplied { int RuleEffectId, int PlayerId }`
- `GameEnded { int WinnerId, ScoreEntry[] Scores }`

`DecisionRequested` carries `playerId` — every client knows whether to prompt or display "waiting on Alice…"

### What gets built

- Server side:
  - `GameSession` runs only on host. Validates every intent against canonical state.
  - `NetworkDecisionRouter`: when an effect requests a decision, server emits `DecisionRequested` and parks the effect on a `TaskCompletionSource<DecisionResolution>` keyed by `requestId`. Resumes when matching `ResolveDecisionIntent` arrives from the right player.
  - Intent validators: turn check, card-in-hand check, decision-belongs-to-this-player check.
- Client side:
  - `ClientGameView`: denormalized snapshot of what this client knows. Built from `DomainEvent` stream.
  - UI subscribes to `ClientGameView` events ("hand changed," "board changed," "current player changed"), not core game events.
  - [DragAndDrop.cs](Assets/Scripts/DragAndDrop.cs) sends `PlayCardIntent`. No local mutation. Authorization becomes server's problem.
  - [BoardUI.cs](Assets/Scripts/UIEntities/BoardUI.cs) + [PlayerHandUI.cs](Assets/Scripts/UIEntities/PlayerHandUI.cs) listen to `CardMoved` events and animate.
- [GameNetworkController.cs](Assets/Scripts/Network/GameNetworkController.cs) collapses: 4 specialized Cmd/Rpc pairs become 2 generic ones (`CmdSendIntent`, `RpcBroadcastEvent` — or use Mirror's `NetworkMessage`s).

### What does NOT change

- Still 2 players (we test at 2P first; 4P comes in Phase 4).
- Card behavior (verified against Phase 0 checklist again).

### Exit criteria

- A modified client cannot play out of turn (server rejects).
- Killing the engine on a non-host client doesn't break anything (clients are passive renderers).
- 2-player happy path works.
- All 18 cards' behaviors still match the checklist.

### Risks

- **Animation/event ordering.** Today UI events fire synchronously and effects can `await AnimationManager.WaitForAllAnimations`. After this phase, events arrive over the wire. Two options:
  - (a) Server paces events: emit `CardMoved`, wait for client `AnimationDone` ack from active player, emit next event.
  - (b) Client buffers events into an animation queue and plays them sequentially.
  Recommend **(b)** — simpler, no server-side animation knowledge, server runs at full speed. Each client has its own `EventQueue` that drains into animations.
- **Effect suspension on server.** `async Task` with the parked-TCS pattern works but needs cancellation if the deciding player disconnects. Per non-goals, we ignore disconnects in v1 — but at minimum, log the parked-decisions count so we notice if effects pile up.
- **State sync at game start.** Initial deck order needs to be sent in `GameStarted`; clients use it to render the deck visually (face-down) without knowing actual card identities until `CardDrawn`.

---

## Phase 4 — 4-player support

**Goal:** the game accepts 2-4 players, host clicks Start, seats arrange around the table.

### What gets built

- Lobby: [GameConnectionManager.cs](Assets/Scripts/Network/GameConnectionManager.cs) accepts up to 4 connections. Host has a "Start Game" button (enabled at 2+ players). Auto-start at 2 is removed.
- [MenuManager.cs](Assets/Scripts/MenuManager.cs): show player list in the lobby with their names; host sees Start button, joiners see "Waiting for host…"
- ~~`SeatLayout` component: arranges N-1 opponent seats around the table (1 → top, 2 → top + opposite, 3 → top + left + right).~~ **Superseded — see the Phase 4.3 entries in Progress.** As built, opponents go along the *top edge* (1 → top centre, 2 → top corners, 3 → three across the top), and placement comes from explicit per-player-count slot tables rather than any "around the table" formula. A circular arrangement was tried and abandoned: a 16:9 screen with a fixed deck, discard, time-of-day slot and common table has no room for it.
- Opponent visualization: card backs only with a count badge. Names + scores shown per seat.
- "Waiting on Player X…" banner: subscribes to `DecisionRequested`; if `playerId != localId`, show banner with that player's name.
- Audit each card's effect for "the other player" assumptions. Specifically check:
  - Effects targeting "the opponent" — replace with `PickPlayer` decision request, candidates = all other players.
  - Effects iterating opponents — make sure they iterate `Players.Where(p => p != active)`, not a hardcoded `LocalOpponent`.
  - Card target enums: do any cards target a specific player slot (e.g., "the player to your left")? Adjust accordingly.
- Win condition audit: confirm "highest score after deck empty" logic loops over all players, not pairwise. Tiebreak: leave undefined for v1, log a known issue if hit.

### What does NOT change

- Server-authoritative model (Phase 3) carries over unchanged. All Phase 4 work is data-driven (player count) and UI.
- Card behaviors (re-verified against checklist with 4 players).

### Exit criteria

- 4 players can join one room and play to completion.
- Each card's behavior matches the checklist when run with 4 players.
- Host disconnects → game ends gracefully (or: log a known limitation if we don't handle it; per non-goals).

### Risks

- Hand layout at 4P with full hand (~5 cards each = 15 opponent cards visible). Just visual polish; not blocking.
- Cards designed assuming 1v1 dynamics. We won't know until the audit. Mitigation: do the audit *before* coding Phase 4 changes.

---

## Phase 5 — Cleanup & polish

**Goal:** remove debt accumulated through migration; make the codebase a place you actually enjoy returning to.

- [x] Delete dead code: `UnityGameBoard.cs`, `GameVisualManager.cs`, `INetworkController.cs`, `MockController.cs`. All four verified gone.
- [x] Fix typos: `AnumateCardTransformToPositionInLayout` → `AnimateCardTransformToPositionInLayout`, `ClearContentPanelchildren` → `ClearContentPanelChildren`. Both verified absent from `Scripts/`.
- [x] **`ItemVIewService.cs` rename recorded in git.** The file had been renamed on disk but git still tracked the old spelling — Windows is case-insensitive, so the rename never reached the index and `git status` looked clean. Fixed with a two-step `git mv --force` through a temporary name (both `.cs` and `.cs.meta`, so the Unity guid survives). Watch for this trap on any future case-only rename.
- [ ] `Libreries` → `Libraries` (folder rename). Still deferred — high blast radius across namespaces and imports. Same case-sensitivity trap as above; use the two-step `git mv`.
- [x] **Rules no longer hardcoded in two places.** New [RuleCatalog.cs](Assets/Libreries/ScaryTales/Rules/RuleCatalog.cs) in the core library maps stable ids to `Rule` instances and sets `Rule.Id`, which had been declared but never assigned. The server picks via `GameNetworkController._inGameRuleId` / `_finalRuleId` (serialized ints, defaulting from the catalog) and sends them in `GameStartedEvent.CurrentRuleId` / `CurrentFinalRuleId` — **fields that already existed but were being sent as literal `0` and never read.** `ClientGameView` stores them; `UnGameManager.HandleGameStarted` rebuilds the rules from the catalog instead of doing its own `new A1()` / `new B2()` in `Awake`, and logs an error if the server names a rule this build doesn't know.
  - Catalog ids are wire format: **append only, never renumber.**
  - `_currentRuleInGame` is now null before the first game starts, so rule readers go through `UnGameManager.RuleEffects()`, which returns an empty list instead of throwing. Matters because the rules button exists on the menu screen.
  - Deliberately **not** a `ScriptableObject`. The debt was the duplication and the dead wire fields, and a catalog fixes both; an asset type for choosing between two rules that have exactly one valid pairing would be plumbing for a feature that doesn't exist. When the lobby picker lands, it drives those two serialized ints — no asset needed.
  - Known wart left alone: `Rule.Effects` is a property that allocates a **new list of new effect instances on every call**, so the effect the server applies is never the same object the client clicked. Effects are stateless today, so it works; if that changes, this is where it breaks.
- [x] Animation delay is a serialized field: [BoardUI.cs](Assets/Scripts/UIEntities/BoardUI.cs) `_animationDelay` (2000 ms pause before a card animates to the discard pile or the time-of-day slot). Since it is a newly serialized field, existing scenes keep the 2000 default rather than picking up a stale value.
- [ ] Update `README.md` with a one-paragraph architecture diagram (lobby → server engine → events → clients).
- [ ] Sweep for stale comments, especially `не работает` markers.

**Exit criteria:** repo is in a state you'd be happy to onboard a collaborator into.

---

## Phase 6 — Dedicated server with multiple rooms

**Goal:** one headless process on our own VM holds several concurrent rooms. A player creates a room by name, gets a short code, friends join by code. No IP entry, no port forwarding, no VPN tool.

**Not a rewrite.** The game engine, all card effects, the `DomainEvent` protocol, `ClientGameView` and the entire UI are untouched. What changes is the thin *addressing* layer: who a packet is sent to, and which room a received packet belongs to.

### Why the engine is already ready (verified 2026-08-21, re-check if it drifts)

The expensive property — several `GameSession`s coexisting in one process — is already true:

- The whole `Assets/Libreries/ScaryTales` core has exactly **two** `static` members: `GameBuilder.MakeCardTemplates()` and `MakeItemTemplates()`. Both are pure factories with no state.
- `Assets/Scripts/Network` has exactly **one** mutable static — `GameNetworkController.Instance` — and **nothing reads it**. It is dead; delete it.
- `GameSession`, `GameManager`, `GameBoard`, `NetworkDecisionRouter` all receive their state through constructors. `InitializeGame(players, connectionMap)` already takes its roster and connections as parameters rather than reading globals.
- `Player.Id` is `conn.connectionId`, which is unique server-wide, so player ids never collide across rooms. Card ids *do* repeat across rooms, and that is fine once events are room-scoped — a client only ever sees its own room.

Reference implementation of this pattern lives in the vendored `Mirror/Examples/MultipleMatches`.

### 6.1 — Disconnect handling (**do this first**)

> ✅ Code-complete, pending in-editor verification. See the Phase 6.1 entry in Progress above for what actually landed — including one hole this original list did not anticipate (cancelling parked TCSs does nothing if the turn loop is *running* rather than awaiting).

Blocking prerequisite. On the host model a dropped player was survivable; on a 24/7 server a hung room leaks forever with nobody to restart it.

- [x] `NetworkDecisionRouter` parks a `TaskCompletionSource` per pending decision and waits forever. Needs cancellation when the deciding player leaves, so the room can end or advance instead of wedging.
- [x] Mid-game disconnect is a logged no-op. Decide and implement the actual policy (end the room, or continue without the player). → **Ends the room**, in `GameConnectionManager.OnSeatVacated`.
- [x] **Known bug on this path:** `OnServerDisconnect` dereferences `conn.connectionId` when `conn` is null during `StopHost()` teardown. Reachable via `ReturnToMenu()` → `StopHost()` → `StopClient()` → `OnClientDisconnect()` → `ReturnToMenu()` — which is **re-entrant**. Fix the re-entrancy, not just the null.
- [x] Log the parked-decision count so wedged rooms are visible. → `GameNetworkController.PendingDecisionCount`, logged on every abort.

### 6.2 — Room-scoped delivery

> ✅ Code-complete. See the Phase 6.2 entry in Progress above. The counts below were the pre-6.1 figures; the actual conversion was 17 call sites, and the "give each room its connection list" step became [RoomChannel.cs](Assets/Scripts/Network/RoomChannel.cs).

The single largest mechanical change, and the one that makes rooms actually isolated.

**22 call sites currently use `NetworkServer.SendToAll`**, which would blast every room's events to every connected player:

| File | Count |
| :--- | :--- |
| [ServerEventBroadcaster.cs](Assets/Scripts/Network/ServerEventBroadcaster.cs) | 14 |
| [NetworkDecisionRouter.cs](Assets/Scripts/Network/NetworkDecisionRouter.cs) | 5 |
| [GameNetworkController.cs](Assets/Scripts/Network/GameNetworkController.cs) | 2 (`TurnAdvancedEvent`, `GameEndedEvent`) |
| [GameConnectionManager.cs](Assets/Scripts/Network/GameConnectionManager.cs) | 1 (`LobbyStateUpdate`) |

Give each room its connection list and replace every one with a `SendToRoom` helper. Mechanical, but **miss one and rooms leak into each other** — grep for `SendToAll` must return zero hits in `Assets/Scripts/` when done.

### 6.3 — One handler set, dispatched by room

> ✅ Code-complete — see the Phase 6.3 entry in Progress above. Landed as [ServerIntentDispatcher.cs](Assets/Scripts/Network/ServerIntentDispatcher.cs). The "test with two rooms from the very first commit" instruction below **could not be honoured**: no second room can exist until 6.4. Treat that as deferred to 6.4's first test, not as done.

The only genuinely architectural change. **Mirror keeps one handler per message type, process-wide.** Today `GameNetworkController.InitializeGame` registers `PlayCardIntent` / `UseRuleEffectIntent`, and the `NetworkDecisionRouter` constructor registers the four `Resolve*Intent`s — per game. With two rooms the second registration silently replaces the first.

Register each message type **once** at server start; the handler resolves the sender's room from a `connectionId → Room` index and dispatches into it. The per-room validation that already exists (`expectedConn != conn`) stays as-is inside the room.

### 6.4 — Room registry and lifecycle

> **Split into 6.4a and 6.4b.** 6.4a (`Room` extraction, engine off `NetworkBehaviour`, one room, no behavior change) is ✅ code-complete — see Progress. 6.4b is the rest of this section: codes, create/join messages, and per-room destruction, to be done together with [6.6](#66--client) because the messages are useless without a client that sends them. The "simplification worth taking" below was taken in 6.4a.

[GameConnectionManager.cs](Assets/Scripts/Network/GameConnectionManager.cs) currently holds one `_players`, one `_connectionMap`, one `_gameStarted`. It becomes a registry:

- `Dictionary<string /*code*/, Room>` plus `Dictionary<int /*connId*/, Room>` for reverse lookup.
- `Room` owns what used to be process-global: roster, connection map, `GameSession`, its `NetworkDecisionRouter`, its `ServerEventBroadcaster`, its turn loop, its started/finished flag.
- New wire messages: create room (name → code), join by code, leave, plus failure cases — unknown code, room full, game already started.
- Code generation: short, unambiguous, case-insensitive. Avoid characters that look alike.
- Destroy a room when it empties, or the server accumulates dead rooms.

**Simplification worth taking here:** `GameNetworkController` is a `NetworkBehaviour` spawned from a prefab via `NetworkServer.Spawn`, but Phase 3.3 deleted every `Cmd`/`Rpc` from it. It has no networked behaviour left — only `[Server]` attributes and the dead `Instance`. Make it a plain C# object owned by `Room`; the prefab, the `spawnPrefabs` entry and the spawn call all go away, and per-room instancing becomes natural.

### 6.5 — Headless entry point

> ✅ Code-complete — see the Phase 6.5 entry in Progress above. One item was deliberately **not** done: `autoCreatePlayer` stays on, for the reason recorded there.

- `NetworkManager.headlessStartMode` → `AutoStartServer` (currently `DoNothing`). No `StartServer()` call exists in `Scripts/` today — only `StartHost()` / `StartClient()` in `MenuManager`.
- `_autoStartWhenFull` is `0` and `StartGameNow()` is driven by the lobby button. A dedicated server has no host player — the room's creator sends a start intent instead.
- [`ReturnToMenu()`](Assets/Scripts/Network/GameConnectionManager.cs#L118) calls `SceneManager.LoadScene`, meaningless headless. Split "reset the room" from "show the menu".
- `GameScene`'s UI components still instantiate on a headless build. They idle harmlessly (no `NetworkClient`, so no `GameStartedEvent`), but a server-has-no-UI guard or a separate bootstrap scene is cleaner. `UnityNotifier` is only `Debug.Log`, so the core library is headless-safe.
- Consider `autoCreatePlayer = 0`: `playerPrefab` is `NetworkPlayerInput`, an empty shell since Phase 5.1.

### 6.6 — Client

Smallest part. `ClientGameView` needs **no changes at all** — a client only ever sees its own room.

- [MenuManager.cs](Assets/Scripts/MenuManager.cs): server address becomes a constant (our VM), the text field becomes room name (create) or room code (join). `StartHost()` is gone — clients only ever `StartClient()`.
- [LobbyManager.cs](Assets/Scripts/UIEntities/LobbyManager.cs): mostly survives. `NetworkServer.active` no longer identifies the host — "who may press Start" becomes a room-creator flag sent from the server.
- Show the room code so it can be read out to friends.

### Exit criteria

- Two rooms play simultaneously on one server process; neither sees the other's cards, turns, decisions or scores.
- `grep -rn "SendToAll" Assets/Scripts/` returns nothing.
- A player disconnecting mid-game does not wedge their room, and does not disturb the other room.
- Rooms are freed when empty; server memory is flat across many sequential games.
- No client ever types an IP address.

### Risks

- **Leaked cross-room delivery** is the defining failure mode. Every new server→client send must be room-scoped by construction, not by remembering.
- Handler-per-room silently overwriting is invisible until a second room exists — test with two rooms from the very first commit of 6.3, not at the end.
- The server turn loop is `async void` with a `try/catch`. With many rooms, an unobserved exception in one loop must not take down others.

---

## Behavior parity checklist

Fill in during Phase 0. One line per card: what does the player *observe* when this card is played? Used to verify Phase 1 effect rewrites and Phase 4 multiplayer scaling didn't change behavior.

Format: `- [ ] CardName — observable effect (target, points, side effects)`

- [ ] CharmCard — _<fill in>_
- [ ] CursedCastleCard — _<fill in>_
- [ ] DarkLordCard — _<fill in>_
- [ ] DayCard — _<fill in>_
- [ ] DragonCard — _<fill in>_
- [ ] EnchantedForestCard — _<fill in>_
- [ ] FairyCard — _<fill in>_
- [ ] FollyKingCard — _<fill in>_
- [ ] HiddenCaveCard — _<fill in>_
- [ ] MerchantCard — _<fill in>_
- [ ] NightCard — _<fill in>_
- [ ] NightChildCard — _<fill in>_
- [ ] OgreCard — _<fill in>_
- [ ] OldMasterCard — _<fill in>_
- [ ] PrincessCard — _<fill in>_
- [ ] WisdomKingCard — _<fill in>_
- [ ] WizardCard — _<fill in>_
- [ ] YoungHeroCard — _<fill in>_

Also note end-of-game rules (currently `A1` initial + `B2` final):
- [ ] Rule A1 — _<observable effect>_
- [ ] Rule B2 — _<observable effect>_

---

## Playing over the internet: how the hosting decision was made

*Recorded 2026-08-21. Keeps the rejected options on file so they don't get re-proposed.*

### The hard constraint

**Somebody must own a machine with a public IP.** NAT forbids inbound connections; no library changes that. The only question is whose machine it is and who pays. "Free", "no third-party service" and "reliable" cannot all be true at once.

Today [MenuManager.cs:16-17](Assets/Scripts/MenuManager.cs#L16-L17) sets `networkAddress` from a text field and calls `StartHost()`; the joiner types the same address. Transport is `KcpTransport`, raw UDP on 7777. A friend connects only by reaching the host's IP:7777 directly — which is why a VPN tool was needed.

### Options weighed

| Option | Verdict |
| :--- | :--- |
| Port-forward UDP 7777 on the host's router | **Rejected.** Requires the host to configure a router and know their public IP. Target users are ordinary people. Dead on carrier-grade NAT regardless. |
| Third-party relay — Edgegap | **Rejected: paid / account-gated.** Technically the fastest path and the code is already vendored (`Mirror/Transports/Edgegap/EdgegapLobby/EdgegapLobbyKcpTransport.cs` even provides a room browser: host calls `SetServerLobbyParams(name, capacity)`, joiner sets `networkAddress = lobby.lobby_id`). Needs an Edgegap account to provision `lobbyUrl`. Keep on file if the hosting decision is ever revisited. |
| Steam relay (FizzySteamworks) | **Rejected for now.** Best NAT traversal available and free once shipped, but Steam Direct is a paid, separate track. |
| Tailscale / ZeroTier | **Rejected.** These *are* Hamachi with a different name. |
| UPnP auto port-open | **Rejected as sole mechanism.** Free and invisible to the user, but fails wherever UPnP is off or the ISP uses CGNAT. Possible future optimisation, never the only path. |
| **Own VM running our own server** | **Chosen.** No third party, no per-player cost, and the owner already intends to buy a VM. Free cloud tiers exist (Oracle Always Free is permanent rather than a trial) — *verify current limits and instance availability before relying on one.* |

### Why this also settles the host-vs-dedicated question

Once a VM with a public IP exists, putting a *relay* on it would be strictly worse than putting the *game server* on it — same work, worse result. Running our own server also removes the host-model weaknesses in one move:

| | host model + relay | own server on own VM |
| :--- | :--- | :--- |
| Host closes the game | match dies | match survives |
| Host has a weak uplink | everyone lags | irrelevant |
| Third-party service | required | none |
| Recurring cost | tariff-dependent | none beyond the VM |

An earlier revision of this document recommended the relay path. That advice optimised for "working soonest" and did not account for the free / no-third-party constraint. With that constraint, the dedicated server wins outright.

---

## Open questions / parking lot

Things we deliberately deferred. Revisit if they start mattering.

- **Reconnect handling** (a dropped player rejoining their seat). Still deferred, but **6.1 built the two things it was blocked on**, so it is now an addition rather than a rewrite:
  - *Seat identity that outlives the connection* — **done.** `Player.Id` is a seat id, not `conn.connectionId`, and `GameConnectionManager._connectionMap` is the single rebindable seat→connection link.
  - *Cancellable parked decisions* — **done**, and each `PendingDecision` keeps its `DecisionRequestedEvent` verbatim so a request can be **re-sent** to a returning player instead of only abandoned.
  - What is left: a rejoin intent that proves which seat you were in, flipping `OnSeatVacated` from "abort now" to "hold the room", and re-sending that seat's pending requests on return. **Note the trap:** a grace window is not sufficient on its own — while the room waits, the engine must also stop *asking* the missing seat for new decisions, or it just parks fresh unanswerable requests. That is why 6.1 ends the room rather than half-waiting.
- **Tiebreak rule** when two players end with equal scores. Undefined today; `GameNetworkController.RunGameLoopAsync` just takes `OrderByDescending(p => p.Score).First()`.
- **Spectators.** Not in scope. The event-replay model would make this cheap to add later (a spectator is a client that never sends intents).
- **Replay / undo.** The `DomainEvent` stream is already an event log; serializing it gives you free replays. Out of scope for v1 but architecturally free.
- **Anti-cheat.** Server-authority gives basic protection (can't play cards not in hand, can't move out of turn). Deeper anti-cheat (information hiding for opponent hands) requires not sending opponent card identities to clients in the first place — feasible in this design but not a v1 priority.
- **Matchmaking.** Out of scope even in Phase 6. Rooms are found by code, not by skill or region.

---

## Working agreement

- One conceptual change per PR.
- Each phase merges to main only when its exit criteria are met.
- Behavior parity checklist re-verified at end of Phase 1, Phase 3, and Phase 4.
- Update this document as decisions evolve. The plan is the source of truth; if reality diverges, fix the plan.

---

## Remaining work (as of 2026-08-30, after deployment)

Nothing here blocks playing. Grouped by what kind of decision it is.

### Housekeeping found during Phase 6, deliberately not fixed

- **`MenuScene` is dead weight.** It has no game table and Mirror's scene switching is off, so a build starting there joins a room and shows nothing. It is unticked in Build Settings; delete it or give it a purpose.
- **Both scenes keep a null entry in `spawnPrefabs`**, left by the prefab deleted in 6.4a. Mirror filters nulls, so it is cosmetic.
- **`AnimationManager` and `CursorManager` call `DontDestroyOnLoad` on child GameObjects**, which does nothing (Unity logs a warning; unlike Mirror they do not reparent to root). They therefore do *not* survive scene reloads. Confirm that is intended.
- **`sendRate` is 60 on the server.** The process ticks that many times a second with nobody playing, because `GameConnectionManager.Start` sets `Application.targetFrameRate = sendRate`. A card game does not need 60 Hz; 20–30 halves the idle cost and changes nothing a player can feel. Matters because the VM also hosts another app.
- **`deploy/Dockerfile` has an unapplied fix**: the service user is now created *with* a home directory, which silences `CreateDirectory '/home/...' failed` / `Unable to load player prefs` on every start. Harmless (nothing uses PlayerPrefs) — pick it up at the next server update.

### Phase 5 leftovers

- [ ] **`Libreries` → `Libraries`** — the last structural debt. High blast radius across namespaces and imports, and the same case-only-rename trap that bit `ItemVIewService`: use a two-step `git mv` through a temporary name, for `.cs` and `.cs.meta` both.
- [ ] **`README.md`** — one paragraph on the architecture (lobby → server engine → DomainEvents → clients) and a pointer to [deploy/](deploy/).
- [x] Sweep for `не работает` markers — grep now returns zero.
- The behaviour-parity checklist further up this file is an **unfilled duplicate**; the real one is [docs/CARDS.md](docs/CARDS.md) and it is complete (18 cards + both rules).

### Product decisions, never in scope for the refactor

- **Players cannot choose a name.** Everyone is `Player1..4`, assigned by seat. Now that real people play together, this is the most visible gap: `CreateRoomIntent`/`JoinRoomIntent` would carry a nickname and `Room.TryAddPlayer` would use it instead of the counter. Small change, real payoff.
- **`EnchantedForestCard` is still commented out** of the deck ([GameBuilder.cs:42](Assets/Libreries/ScaryTales/GameBuilder.cs#L42)). The effect was rewritten in Phase 4.2 and is correct for any player count — uncommenting is the whole task.
- **No persistence.** Rooms live in memory, so any server restart (update, reboot, crash) ends every game in progress. Handled cleanly — players are returned to the menu — but there is no resume.
- **Reconnect** — still deferred, but both prerequisites exist (see the parking lot).
- **Tiebreak** on equal scores is undefined; `OrderByDescending(p => p.Score).First()` just picks one.
