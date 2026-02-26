# Stardew Delivery Service — TODO

**Read this before starting any feature.** Items are numbered for reference, not priority order.

Current version: **0.6.10** — Progression gating MVP. Junimo/Shane cutscenes, icon gating, delivery cost.

### Quick Fixes
- **Icon wiring**: Don't wire the SDS icon space in allClickableComponents when progression gating is enabled and the service is not yet unlocked. Currently the component exists (for neighbor wiring stability) even when hidden — this may cause snap navigation to land on an invisible slot.

---

## Phase 1: Progression Gating

### 1. Disable JDS Until First Bundle Completed
- Junimo Delivery Service should be **locked** at game start.
- Unlocks when the player completes their **first Community Center bundle**.
- Before unlock: Junimo icon hidden or greyed out on inventory page. Config toggle still exists but service is gated by progression.
- **Investigation needed:** How does the game track individual bundle completion? Check `Game1.player.bundleData`, `CommunityCenter.bundles`, or similar. Need an event or flag that fires on first bundle turn-in.
- **Save compatibility:** Players who already completed bundles should have it unlocked immediately on mod load.

### 2. CC Exit Cutscene — Junimo Delivery Unlocked
- After the player completes their first CC bundle and **exits the Community Center**, play a cutscene.
- **Cutscene concept:** A Junimo appears outside the CC entrance, does a happy dance, and a text bubble says something like "We'll help you carry things!" or similar. Establishes that the Junimos are grateful and will now assist with deliveries.
- **Implementation:** Use SMAPI's `Event` system or `GameLocation.afterQuestion` / custom event script. Trigger on `Warped` event when leaving CC after first bundle flag is set.
- **One-time only:** Save a flag (`JunimoDeliveryUnlocked`) so it doesn't replay.

### 3. Joja Membership Cutscene — Junimo Service Disabled
- If the player buys a JojaMart membership, the Junimo delivery service is **disabled**.
- Cutscene plays when the player **exits JojaMart** after the membership purchase.
- **Cutscene concept:** A Junimo appears outside JojaMart looking sad/betrayed, then vanishes. Text bubble: "..." or something bittersweet. The Junimos no longer want to help someone who sided with Joja.
- **Investigation needed:** How to detect Joja membership purchase. Check `Game1.player.mailReceived` for `JojaMember` flag or similar.
- **Save flag:** `JunimoDeliveryDisabled` — persistent, prevents JDS from re-enabling.

### 4. Shane Delivery Cutscene — Next Day After Joja Disables JDS
- The morning after the Junimo service is disabled, Shane shows up at the player's door.
- **Cutscene concept:** Shane offers to handle deliveries for a fee. "I could use the extra cash. I'll bring stuff from your chests — for a price." Establishes the paid alternative.
- **Trigger:** Next day after `JunimoDeliveryDisabled` flag is set. Morning event or `DayStarted` event.
- **One-time only:** Save flag `ShaneDeliveryUnlocked`.

### 5. Replace JDS with Shane Delivery Service (SDS)
- When Shane's cutscene plays, swap the delivery backend from Junimo (free) to Shane (paid).
- Same chest browser functionality, but each delivery incurs a cost.
- **State machine:** `DeliveryProvider` enum: `None`, `Junimo`, `Shane`
- Persists in save data via SMAPI's `SaveData` API.

---

## Phase 2: Shane Delivery Service (Paid Mode)

### 6. Shane Delivery Icon
- Replace the Junimo icon on the inventory page with a Shane-themed icon.
- **Options:** Telephone icon, Shane's chicken, beer mug, or custom pixel art (Shane's face/silhouette).
- Could make a small custom sprite — 16x16 to match the Junimo frame size.
- Same click behavior opens the chest browser; same controller navigation.

### 7. Delivery Cost System
- Each delivery through Shane's service costs gold.
- **Cost model TBD — candidates:**
  - Per item (flat fee per item transferred)
  - Per stack (flat fee per stack, regardless of stack size)
  - % of item value (scales with expensive items)
  - Flat fee per chest-open session (simplest)
  - Hybrid: small per-stack fee + % of total value
- **UI:** Show cost before confirming? Or deduct automatically with a HUD message?
- **Edge case:** What if player can't afford delivery? Grey out transfers? Show warning?
- **Investigation needed:** Decide on model, implement gold deduction in the transfer hooks.

---

## Phase 3: Delivery Animations

### 8. Junimo Delivery Animation (JDS)
- When the player exits the chest browser (closes the ItemGrabMenu while JDS is active), a Junimo appears near the player.
- **Animation:** Small Junimo walks up, does a little bounce/wave, then fades out or walks off-screen.
- Use the game's `TemporaryAnimatedSprite` or spawn a temporary NPC/character.
- The Junimo should carry a small bundle/package sprite if possible.

### 9. Shane Delivery Animation (SDS)
- Same trigger as #8, but Shane appears instead.
- **Animation:** Shane walks up, maybe shrugs or gives a nod, then walks away.
- Could use Shane's existing sprite sheet for walk/idle animations.
- Simpler approach: `TemporaryAnimatedSprite` with Shane's sprite, or briefly spawn Shane's NPC at the player's location.

### 10. Delivery Text Bubbles
- Each delivery animation (#8/#9) includes a speech bubble with a random phrase.
- **Junimo phrases** (cute, nature-themed):
  - "Here you go!"
  - *happy bouncing noises*
  - "The forest provides!"
  - "We found it!"
  - "For our friend!"
  - (more to write — aim for 15-20 phrases)
- **Shane phrases** (grumpy but helpful):
  - "Here. Don't say I never did anything for you."
  - "That'll be [cost]g."
  - "You're lucky I needed the exercise."
  - "Jas says hi, by the way."
  - "Next time, organize your chests better."
  - "Charlie almost ate one of these."
  - (more to write — aim for 15-20 phrases)
- Could use `Game1.drawDialogueBox` or a custom floating text sprite.

### 11. Junimo Icon Color Cycling + Matching Delivery Junimo
- The Junimo icon on the inventory page **slowly cycles through colors** (or changes color each day/session).
- Junimo colors: green, blue, red, yellow, purple, orange, cyan, pink (match the CC Junimo palette).
- When the delivery animation plays (#8), the Junimo that appears is the **same color as the current icon**.
- **Narrative:** Reinforces that all the Junimos are pitching in — a different one helps each time because you're a friend of nature and they all want to contribute.
- **Implementation:** Tint the icon sprite with a color from the palette. Track `currentJunimoColorIndex` and rotate on some interval. Pass the color to the delivery animation.

---

## Phase 4: Polish & Progression

### 12. Bundle Completion Progression — Junimo Excitement Scaling
- As the player completes more bundles, the delivery animations get more elaborate:
  - 1 bundle: Single Junimo, simple wave
  - 3+ bundles: Junimo does a little dance
  - All bundles (CC complete): Rare chance for multiple Junimos to appear together, confetti/sparkle effect
- The multi-Junimo event is **rare and special** — a delightful surprise reserved for players who fully completed the CC. Not every delivery, just an occasional celebration.
- Rewards the player's CC investment with increasingly delightful animations.

### 13. Shane Friendship Integration
- Shane's delivery cost could decrease as friendship increases.
  - 0-2 hearts: Full price
  - 4-6 hearts: 10% discount
  - 8+ hearts: 25% discount
  - 10 hearts: "Ah, forget it. This one's on me." (occasional free delivery)
- Shane's dialogue pool expands with friendship (warmer phrases at higher hearts).

### 14. Delivery Log / History
- Optional: track what was delivered, when, cost (for Shane). Viewable somewhere — maybe a second tab in the chest browser, or a journal entry.
- Low priority, nice-to-have.

### 15. Seasonal Shane Dialogue
- Shane's delivery phrases change with the season or special events.
  - Winter: "Do you know how cold it is out here?"
  - Spring: "At least the walk was nice."
  - Festival days: "I'm missing the festival for this."
  - Rainy days: "You owe me an umbrella."
  - After Emily events: references her

### 17. Junimo Gifts — Foragables & Resources Based on CC Progress
- As the player completes more bundles, the Junimos occasionally bring **random gifts** when delivering items.
- Gifts appear as bonus items after closing the chest browser — the delivery Junimo drops them.
- **Loot table scales with CC progress:**
  - Few bundles: common foragables (fiber, sap, wood, stone, mixed seeds)
  - More bundles: seasonal crops, flowers, basic ores
  - Many bundles: gold ore, gems, quality crops
  - CC complete: rare chance for diamonds, iridium ore, prismatic shard (extremely rare)
- **Probability scales with bundle count:** More bundles = higher chance of gift per delivery AND higher tier loot.
- **Thematic connection:** The items should relate to donated bundle categories — Pantry bundles increase crop gifts, Fish Tank increases beach forage, Boiler Room increases ores/gems, etc.
- **Implementation:** Build a weighted loot table keyed by completed bundle areas. Roll on delivery close. Spawn item via `Game1.player.addItemByMenuIfNecessary()` or drop on ground.
- Low-priority polish feature. Needs careful balancing to avoid being exploitable.

### 16. Dangerous Location Blocking
- If the player tries to use the delivery service (Junimo or Shane) while in a **dangerous location** (mines, Skull Cavern, volcano dungeon, any location with enemies including SVE areas), the service is **blocked**.
- Instead of opening the chest browser, show a themed rejection message.
- **Junimo messages** (scared, protective):
  - "It's too scary here! We can't come!"
  - "The shadows... we'll wait for you outside."
  - "We sense danger. Please be safe, friend!"
  - "Not here... the dark things would see us."
- **Shane messages** (practical, self-preserving):
  - "Yeah, I'm not going down there."
  - "You want me to deliver to a MINE? Hard pass."
  - "I don't get paid enough for monster territory."
  - "Call me when you're somewhere I won't get eaten."
  - "Jas needs her godfather alive, thanks."
- **Detection:** Check `location.IsOutdoors`, `location is MineShaft`, `location is VolcanoDungeon`, or check for `location.characters` containing monsters. For SVE compatibility, check location name patterns or a configurable blocklist.
- **Implementation:** Gate check in `ChestBrowser.Open()` before scanning chests. Show HUD message with random phrase from the appropriate pool.

---

## Technical Notes

### Save Data
- Use SMAPI's `helper.Data.WriteSaveData<T>` / `ReadSaveData<T>` for:
  - `DeliveryProvider` (None/Junimo/Shane)
  - `JunimoUnlocked` (bool)
  - `ShaneUnlocked` (bool)
  - `CurrentJunimoColor` (int index)
  - Cutscene-played flags

### Cutscene Implementation
- Stardew Valley events use event script strings (`/speak Shane "text"/pause 500/...`)
- Can inject custom events via `GameLocation.currentEvent` or `Event` constructor
- Alternative: use `Game1.activeClickableMenu` with a custom dialogue/animation sequence
- **Investigation needed:** Which approach is more reliable on Android? Check how other mods (Event Repeater, Custom Events) handle this.

### Compatibility
- Must work on both Android (SMAPI for Android) and PC (standard SMAPI)
- Cutscene/animation code needs to handle both touch and controller input for dismissal
- All features individually toggleable via GMCM
