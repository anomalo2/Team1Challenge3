# Mech gameplay systems — setup

Grab / crush, city traffic, and the "help her across" challenge.

Everything here is **decoupled from the mech**. No script references a mech
type, a hand bone, or an XR rig. The mech developer attaches two components to
bones when the mech exists and nothing else has to change.

All 11 scripts compile clean against Unity 6000.3.25f1 (warning level 4).

> ## Content rule: no death, no maiming
>
> Per the assignment brief, **no civilian can be killed or injured.** This is
> enforced in code in three independent places, not left to inspector
> discipline — see §4. The failure state is that a civilian gets **frightened**
> and retreats to the kerb to try again, which costs time and score and nothing
> else. There is no harm state and no code path that reaches one.
>
> Property — cars, lamp posts, dumpsters — is still fully destructible. See
> §4a for the one open question this raises.

---

## 1. The one idea

`CrushableObject` owns a single number — structural integrity — and exposes two
ways to spend it:

| Method | Used for | Units |
|---|---|---|
| `ApplySustainedCrush(force)` | a closing hand, a foot resting on the roof | newtons, per frame |
| `ApplyImpact(impulse)` | a stomp, a thrown car landing | newton-seconds, one shot |

Anything that can produce a number can damage anything else. That is why the
hand and foot components are ~100 lines each, and why you can test the whole
game loop before the mech is modelled.

**Force below `crushResistance` does literally nothing.** That is the mechanic:
a gentle grip is always safe, so "pick up or crush" is a skill, not a dice roll.

### Scripts

| Script | Goes on | Job |
|---|---|---|
| `Crush/CrushableObject.cs` | cars, props, people | integrity, denting, flattening, detonation, chain blasts |
| `Crush/MechGrabber.cs` | a hand grab volume | grab / carry / squeeze / throw |
| `Crush/HandTrackedGripSource.cs` | the same hand volume | hold from finger curl, crush from wrist twist (hand tracking has no trigger) |
| `Crush/MechFootCrusher.cs` | a foot volume | stomp + standing weight |
| `Crush/CrushOnImpact.cs` | anything throwable | feeds physics collisions into crush damage |
| `Core/CityObjective.cs` | one scene object | the scoreboard |
| `Traffic/TrafficRoute.cs` | an empty with child empties | a lane, or a crossing |
| `Traffic/CarController.cs` | car prefab | drives the lane, brakes for obstacles |
| `Traffic/TrafficSpawner.cs` | a scene object | keeps the lane populated |
| `Civilians/PedestrianCrosser.cs` | the old lady | walks, waits, panics, dies, gets rescued |
| `Civilians/CrosswalkChallenge.cs` | a scene object | arms the objective, decides win/lose |

---

## 2. Layers to create first

Project Settings ▸ Tags and Layers. The project currently has **no custom
layers**, and the masks below will not work until these exist:

| Layer | Put on |
|---|---|
| `Mech` | mech colliders |
| `Vehicle` | car prefabs |
| `Civilian` | pedestrians |
| `Crushable` | destructible props |

Then set the masks:

- `MechGrabber.grabLayers` → Vehicle, Civilian, Crushable
- `MechFootCrusher.crushLayers` → Vehicle, Civilian, Crushable
- `CarController.obstacleLayers` → Mech, Vehicle *(not Civilian — see §5)*
- `CarController.pedestrianLayers` → Civilian
- `PedestrianCrosser.vehicleLayers` → Vehicle

---

## 3. Car prefab

Use any `MetroFleet` prefab, e.g. `EnvironmentAssets/MetroFleet/Prefabs/Civilian/`.
Add, in this order:

1. **Rigidbody** — mass ~1500. Leave the rest default; `CarController` sets
   `isKinematic` itself.
2. **Box Collider** — not a trigger. Layer `Vehicle`.
3. **CarController** — leave `route` empty, the spawner assigns it.
4. **CrushableObject**
5. **CrushOnImpact** — so a thrown car detonates when it lands.

Car values (these are the tuned defaults):

| Field | Value |
|---|---|
| `crushResistance` | 8000 |
| `structuralIntegrity` | 60 |
| `sustainedCrushRate` | 0.003 |
| `impactCrushRate` | 0.001 |
| `canExplode` | ✔ |
| `explodeForceThreshold` | 50000 |
| `propertyValue` | 25 |

The car is **kinematic while driving** and flips to a dynamic body the moment
it is grabbed or wrecked. That keeps lanes tidy and stops the mech bulldozing
traffic by brushing past it, while still letting wrecks tumble and be thrown.

### Lanes

1. Empty GameObject `Lane_Northbound`, add **TrafficRoute**.
2. Child empties along the road centre, in order. Leave `waypoints` empty and
   it auto-fills from the children on Awake.
3. `loop` ✔ for ring roads; ✘ for a lane that runs off the map
   (`CarController.despawnAtEnd` then cleans up).
4. Add a **TrafficSpawner**, point it at the route, drop 3–4 car prefabs into
   `carPrefabs`. `spawnInterval` 4, `maxAlive` 8, `minSpacing` 8.

Gizmos draw the path and flow direction when the route is selected.

---

## 4. The old lady

Any humanoid from `CharacterAssets`. Add:

1. **Rigidbody** — mass 70, `isKinematic` ✔ (the script manages it).
2. **Capsule Collider** — layer `Civilian`.
3. **CrushableObject**
4. **PedestrianCrosser**

Her `CrushableObject` is where the whole theme lives:

| Field | Value | Why |
|---|---|---|
| `canBeDestroyed` | **✘** | **she can never be destroyed. Non-negotiable.** |
| `crushResistance` | 400 | below this, crushing force does nothing at all |
| `structuralIntegrity` | 8 | how much rough handling before she wriggles free |
| `sustainedCrushRate` | 0.02 | |
| `canExplode` | **✘** | never detonate a person |
| `countsAsPropertyDamage` | **✘** | scored as a person, not a repair bill |

### The no-harm rule is enforced in code, three ways over

`PedestrianCrosser.EnforceNonLethalSetup()` runs in `Awake` and force-corrects
`canBeDestroyed`, `canExplode` and `countsAsPropertyDamage` with a loud console
warning if anyone sets them wrong. On top of that:

1. **`CrushableObject.canBeDestroyed = false`** makes destruction
   unreachable. Every destruction path is gated on it, including the
   instant-detonation shortcut and `DestroyNow` itself. Instead of being
   destroyed, the object floors its integrity and raises **`Overstressed`**.
2. **Squeezing her makes her squirm free.** `MechGrabber` releases anything
   that reports `IsOverstressed`, which also ends the squeeze. She retreats,
   unhurt.
3. **Cars emergency-stop.** `CarController` casts ahead on `pedestrianLayers`
   and, on detecting her, sets speed to zero and holds for
   `emergencyStopDuration` *before* notifying her. Nobody is ever run over —
   the near miss is the entire consequence.

So the worst outcome in the game is a frightened pedestrian hurrying back to
the kerb.

Her crossing is just another `TrafficRoute` — 3–4 waypoints, kerb to kerb,
`loop` **✘**. Assign it to `crossRoute`.

### Two ways to win

Both work, deliberately:

1. **Block the road.** Cars brake for anything on `obstacleLayers`, and she
   only treats a *moving* car as a threat. Park the mech in the lane and she
   walks across by herself.
2. **Carry her.** Grab gently, set her down within `arriveRadius` of the last
   waypoint. Handle her roughly and she wriggles out of your grip and retreats
   to the kerb — shaken, unhurt, and you have to start again.

She waits for a gap for `patience` seconds (default 12) and then **steps out
anyway**. That timer is the pressure on the player. Mid-road she freezes when
a car gets within `panicDistance`, which buys you a moment to intervene.

### Failure is a setback, not a loss

A scare sends her back to the kerb at `retreatSpeed` (1.6 m/s — she hurries,
so it genuinely costs you). Then she waits and tries again. `ScareCount`
tracks it, and `CrosswalkChallenge.maxScares` (default 3) decides how many
retries you get before the objective is marked failed.

### The challenge object

Empty GameObject at the junction, add **CrosswalkChallenge**:

- `pedestrian` → the old lady
- `player` → mech root, or the XR Origin until the mech exists
- `armRadius` 30 — arms when the player gets close
- `trafficDuringChallenge` → the spawners for that junction
- `timeLimit` 0 to start with
- `maxScares` 3 — retries before the objective fails

---

## 4a. Open question — are the cars occupied?

Worth settling before this goes near a marker. Cars are destructible and
**detonate** when crushed hard enough. If the fiction says those cars have
drivers in them, then blowing one up is a death, and the no-death rule is
broken in spirit even though no civilian object is ever harmed in code.

Cheapest fix is a line of fiction rather than a code change: make city traffic
explicitly **autonomous and empty** — self-driving pods, freight shuttles, a
cordoned-off test district. Then crushing a car is pure property damage, which
is exactly what the scoring already treats it as.

If your brief needs something stronger, the alternatives are to set
`canExplode` ✘ on all vehicles so they only crumple, or to give them
`canBeDestroyed` ✘ too so they just dent. Both work today with no code change.

I have left the cars destructible and explosive, since that is what was asked
for, and flagged it here rather than quietly changing the brief.

## 5. Why cars don't brake for her

`yieldToPedestrians` is **off** by default and `Civilian` is deliberately left
out of `obstacleLayers`. If cars stopped for her the challenge would solve
itself. Turn it on only for a tutorial junction.

Cars detect her with a short `SphereCast`, not a collision, because a
kinematic body does not report collisions dependably.

---

## 6. Mech attachment — the handoff

This is all the mech developer has to do.

### Hands

Per hand: empty child GameObject `GrabVolume` on the hand bone, with a
**Sphere Collider, `Is Trigger` ✔** sized to the palm. Add **MechGrabber**.

Then drive the grip. **Read §6a before changing any of this** — finger curl
cannot sense squeeze force, and the reasons are not obvious.

```csharp
// Option A - HAND TRACKING (current setup). Add HandTrackedGripSource to the
//   same object and point its `grabber` field at the MechGrabber.
//   Closed hand = hold, wrist twist = crush. See 6a.

// Option B - CONTROLLERS. Assign MechGrabber.gripAction in the inspector
//   to a grip-axis action. True analog pressure plus rumble.

// Option C - drive it yourself from the mech controller.
grabber.SetGrip(gripValue01);   // every frame, 0..1
```

Useful members:

```csharp
grabber.Grip              // 0..1
grabber.CurrentGripForce  // newtons, for a haptic/audio curve
grabber.Held              // the CrushableObject in hand, or null
grabber.Release();        // force a drop
grabber.onCrushing        // fires while squeezing hard enough to do damage
```

Hook `onCrushing` to controller haptics — the player needs to *feel* that they
are about to crush what they are holding. That one wire does most of the work
of teaching the mechanic.

Held objects are **followed by transform, not reparented**, so a scaled hand
bone cannot stretch a car.

## 6a. Hand tracking has no pressure control

`HandIK.cs` reads `XRHandSubsystem`, so the mech is **hand tracked**, and hand
tracking gives joint poses and nothing else:

- **no analog grip axis** — there is no trigger to read
- **no haptics** — your real hand closes on empty air, so you cannot *feel*
  the car resisting
- **no way to sense grip strength at all** — see below

### Finger curl cannot sense how hard you are squeezing

This is worth being blunt about, because it is the obvious thing to try and it
does not work. `XRFingerShape.FullCurl` is computed from joint **flexion
angles**, normalized against per-finger degree ranges and then clamped:

```csharp
return 1f - math.clamp(totalNormalizedDegrees / divisor, 0f, 1f);
```

For the index finger those ranges bottom out at **102° / 90° / 120°**, and a
merely *relaxed* closed fist already flexes past them. So `FullCurl` pins at
exactly **1.0** the instant the hand shuts, and clenching harder changes
nothing — your fingers are physically blocked by your palm. In that pose they
are also occluded from the headset cameras, so the values are low-confidence
extrapolation as well.

`TipCurl` clamps identically. `Pinch` works in a 10–45 mm band that is inside
Quest's tracking noise. **No finger-shape value can measure grip strength.**

Mapping crush force to curl is actively dangerous: a loosely closed hand reads
~0.95–1.0, so crush force would begin the moment you closed your hand around
the person you were trying to rescue.

### What `HandTrackedGripSource` actually does

Curl is used **only** as a reliable open/closed test (with hysteresis), and
crushing is driven by **wrist roll**, because the wrist is the root joint,
always visible, the most reliably tracked thing on the hand, and forearm
pronation gives a genuinely analog 0–90°+ of travel.

| Action | Result |
|---|---|
| close your hand | **hold only — can never damage anything** |
| twist your wrist | crush, **graded by twist angle** |
| untwist | crushing stops — it is fully reversible |
| snap the fist shut fast | slam: a burst of force |
| relax your hand | release |

A closed hand sits exactly at `grabThreshold`, where the cubic force curve
evaluates to roughly zero newtons. So carrying the old lady across a junction
is free and indefinite, and killing her takes a deliberate twist you can undo.

Twist is measured by **swing-twist decomposition** about the neutral frame's
+Z, which XR Hands documents as pointing along the wrist/forearm. That isolates
roll, so merely *bending* the wrist while carrying something does not register.
The neutral is captured at the moment the hand closes, so the gesture is
relative and does not care how the arm is oriented. `RecenterTwist()` re-zeros
it if it drifts.

Closing **speed** is the one force-like quantity curl can honestly report,
because it is measured during the open→closed transition while the fingers are
still visible. That is why slam survives.

### You must replace the missing sense of touch

There are no haptics, so wire `HandTrackedGripSource.CrushIntent` (0..1) to
something the player can **see or hear** — straining metal, a glowing hand, a
cockpit gauge. `onCrushStarted` fires the moment force begins being applied, as
the warning cue. This is not polish; it is the feedback channel standing in for
feeling the object resist.

Twist angle itself is also worth showing, since the player cannot see their own
wrist inside the mech. `TwistDegrees` is exposed for exactly that.

### Tracking loss is handled deliberately

If tracking blinks out, crush force drops to **zero** — letting a garbage wrist
pose drive it is exactly how you detonate a car or kill a civilian through no
fault of the player. The *hold* is what gets preserved, not the crush: with
`keepHoldingWhenTrackingLost` on (the default) the hand keeps its grip for
`dropAfterTrackingLost` seconds instead of dropping whoever it was carrying.

### Controllers remain the stronger option

If you ever ship on Touch controllers, **delete this component** and assign
`MechGrabber.gripAction` to a grip-axis action. You then get real analog
pressure *and* rumble, so the player can feel the resistance directly and the
fairness problem disappears rather than being worked around. Everything else in
this document is unchanged either way.

### Feet

Per foot: empty child GameObject `StompVolume` on the foot bone, with a
**Box Collider, `Is Trigger` ✔**. Add **MechFootCrusher**.

`onStomp` is already shaped to drive the existing `GroundCracks.SpawnCrack()`.

### The constraint behind both

`ModelDisplacement.cs` writes `transform.localPosition` directly and `HandIK.cs`
poses from hand tracking, so **the mech is animated, not simulated**.
`Collision.impulse` and `Rigidbody.linearVelocity` therefore read **zero** on
every mech limb.

Both components work around this by differencing their own transform in
`LateUpdate` (after IK has posed the bone) and multiplying by a configured
mass. If the mech later becomes a real physics body, these still work — but
switch them to trigger volumes on the bones regardless, which is already the
documented setup.

---

## 7. Tuning tables

### Grip → force

`force = maxGripForce × ((grip − grabThreshold) / (1 − grabThreshold))³`

at defaults `maxGripForce` 80000, `grabThreshold` 0.3, `gripForceCurve` 3:

| Grip | Force (N) | Car (res 8000) | Old lady (res 400) |
|---|---|---|---|
| 0.35 | 29 | safe | safe |
| 0.40 | 233 | safe | safe |
| 0.42 | ~400 | safe | **edge of comfortable** |
| 0.45 | 787 | safe | wriggles free in ~1.0 s |
| 0.50 | 1,866 | safe | wriggles free in ~0.3 s |
| 0.60 | 6,297 | safe to carry | — |
| 0.70 | 14,927 | crumples in ~2.9 s | — |
| 0.80 | 29,154 | crumples in ~0.9 s | — |
| 0.90 | 50,379 | **detonates** | — |
| 1.00 | 80,000 | **detonates** | — |

The cubic curve is what makes the bottom of the trigger pull forgiving. Lower
`gripForceCurve` toward 1 and grabbing anything becomes a knife-edge.

### Crush intent → force (hand tracking)

Under `HandTrackedGripSource` the player does not set grip directly. A closed
hand pins grip to `grabThreshold` and **crush intent** is what spends force:
`grip = lerp(0.301, 1.0, crushIntent)`.

Crush intent comes from **wrist twist**, mapped
`intent = inverseLerp(25°, 80°, |twistDegrees|)`:

| Twist | Crush intent | Force (N) | Effect |
|---|---|---|---|
| 0–25° | 0.00 | ~0 | safe hold, indefinitely — the deadzone |
| 39° | 0.25 | 1,266 | old lady wriggles free in ~0.5 s; car unharmed |
| 52° | 0.50 | 10,060 | car crumples over ~4 s |
| 66° | 0.75 | 33,750 | car crumples in ~0.8 s |
| 80° | 1.00 | 80,000 | car **detonates** |

The mapping is absolute, not accumulated, so **untwisting stops the crushing**.
That reversibility matters: there is no committed point of no return while you
are carrying someone. (A civilian cannot be damaged in any case — the worst
outcome is that they squirm out of your grip and retreat. See §4.)

One slam adds 0.50 instantly and bleeds off at `slamDecaySpeed` 2.0/s.

The 25° deadzone is deliberately generous — carrying something through a turn
must not crush it. If playtesters crush by accident, widen the deadzone first,
before touching any force value.

Narrow the `twistDeadzoneDegrees`/`twistFullCrushDegrees` window to make
crushing twitchier, widen it to make it more deliberate.

### Foot

| Action | Impulse / force | Effect on a car |
|---|---|---|
| step at 2 m/s | 24,000 | dents it (16 of 60) |
| stomp at 3 m/s | 36,000 | half-wrecks it (28 of 60) |
| slam at 4.2 m/s | 50,400 | **detonates** |
| just standing | 45,000 sustained | flat in ~0.5 s |

`standingForce` must stay **below** the car's `explodeForceThreshold` (50,000)
or standing on a car detonates it rather than flattening it. Raise
`effectiveMass` if you want stomps to detonate more readily.

### Scoring

`CityObjective` — one per scene:

- `pointsPerCivilianSaved` 100
- `penaltyPerCivilianFrightened` 150
- `penaltyPerPropertyValue` 1
- `logIncidents` ✔ — prints every incident to the Console, which is the whole
  readout until there is UI

There is no casualty counter, because nothing can produce one. Scaring someone
costs ~6× a wrecked car, so the game still pushes you to treat people more
carefully than property. Hook `onStateChanged` to a cockpit canvas later; the
component has no UI dependency.

---

## 8. Testing without a headset or a mech

The point of the decoupling — you can play most of this today:

1. Empty scene, a ground plane, a `TrafficRoute` + `TrafficSpawner`, a
   `CityObjective`.
2. Press Play. Cars should drive the lane and brake for each other.
3. Drop a cube with a Rigidbody, a Box Collider on `Mech`, and a
   `MechFootCrusher` on a trigger child. Drag the cube onto a car in the
   inspector while playing — the car dents, flattens, and the Console logs the
   damage.
4. For the hand, put a `MechGrabber` on a cube and call `SetGrip()` from a
   three-line test script bound to a key. Feather the value to feel the curve.

Check the Console — `[Objective]` lines report every incident with a running
score.

---

## 9. Not done

I wrote and compile-verified the scripts, but **I could not create the prefabs
or wire a scene** — that needs the Unity editor driving asset import and GUID
generation, which I can't do from the terminal without risking corrupt
`.meta` files. Sections 2–4 are the click-by-click to do it.

Nothing is committed. `git status` to review.
