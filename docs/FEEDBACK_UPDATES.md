# Feedback and updates

ST10440432, Matteo Nusca. PROG7312 Smart-X IoT Mesh Gateway.

This file lists feedback I received on earlier parts and what I changed because of it. Lecturer feedback is kept apart from my own improvements so the two are not mixed up.

## A. Lecturer feedback on Part 1

| Criterion | Mark | Feedback | What I did |
| --- | --- | --- | --- |
| Operator overloading | 6/10 | Use of operator overloading needed to be stronger. | See A1 |
| Recursion | 6/10 | Use of recursion needed to be stronger. | See A2 |

### A1. Operator overloading

* `TelemetryPacket<T>` (SmartX.Shared/Models/TelemetryPacket.cs) has comparison operators (`>`, `<`, `>=`, `<=`) so two readings compare by value.
* `NumericTelemetryPacket<T>` has `+` and `-`. The ingestion pipeline uses `current - previous` to get the change between readings, which drives spike detection, rather than hand-written subtraction.
* New in Part 3: `LinkCost` (SmartX.Shared/Mesh/LinkCost.cs) is a struct that overloads `+`, `<`, `>`, `<=`, `>=`, `==` and `!=`. Dijkstra and Prim add costs along a route with `+` and choose the cheaper candidate with `<`, so the routing algorithms read like the maths.

### A2. Recursion

* `DeploymentZone.Validate` and `ZoneTree` / `ZoneTreeBuilder` walk the facility, zone and node hierarchy recursively (Part 1).
* New in Part 3:
  * `AvlTree` insert, remove, height and range query are recursive. The range query skips whole subtrees that cannot hold a match.
  * `MeshGraph.DfsVisit` is a recursive depth-first search used for connectivity and island detection.
  * `MstResult.PathToRoot` follows parent links back to the gateway.
  * The union-find `Find` in Kruskal's algorithm recurses with path compression.

## B. Lecturer feedback on Part 2

Not yet added. Paste the marker comments here and list the change made for each.

| Criterion | Mark | Feedback | What I did |
| --- | --- | --- | --- |
|  |  |  |  |

## C. My own improvements (not lecturer feedback)

* The history archive and the mesh are covered by unit tests (25 new tests) and run in GitHub Actions on every push.
* Added a `node-failure` simulator scenario so the self-healing route change can be shown without editing code.
* Corrected a stale readme line that still said pillar 3 was disabled.
* Added code examples and a scaling table to the Part 3 readme section.
* Fixed a build error on the Topology page (`@bind` on a `datetime-local` input) found by CI.
