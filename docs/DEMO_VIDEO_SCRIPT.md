# Part 3 demo video script (about 5 minutes)

Before recording: `docker compose --profile sim up --build`, or run the API, client and `dotnet run --project SmartX.Simulator -- --extra 0` in three terminals. Wait about 20 seconds so data is flowing. Open http://localhost:8081 (Docker) or http://localhost:5206.

**1. Intro (0:00)**
"I'm Matteo Nusca, ST10440432. This is Part 3 of Smart-X: network topology and mesh routing. The gateway and the sensors form a weighted graph, where each link has a radio power cost in milliwatts."

**2. Home page (0:20)**
Show the three active pillars, then click Network Topology.

**3. The map (0:35)**
"The gateway is in the centre. Each sensor linked itself to the three cheapest nodes already in the mesh. Same-zone links are cheap, other facilities are expensive. Thick lines are the minimum spanning tree from Prim's algorithm, the cheapest set of links that connects every node."
Point at the stat cards: up, down, MST total against all links.

**4. Routes (1:30)**
Click Pump A. "Breadth-first search gives the fewest hops. Dijkstra gives the cheapest power cost. Prim's tree gives the route along the spanning tree." Switch the route mode and show the highlight change. "The tree route can cost more for one sensor because the tree minimises the total, not each individual route."

**5. Self-healing (2:15)**
Click Fail on a node on that route. "The node is treated as removed. The graph is searched again and the route changes. The panel shows the extra power the reroute costs." Show an isolated node if one appears, then Restore.

**6. Simulator failure (3:00)**
"The simulator's node-failure scenario silences a device. After 60 seconds the watchdog raises a Disconnected alert and the mesh marks the node down by itself." (Say this over a speed-up or cut to the result.)

**7. History and the AVL tree (3:30)**
Open the history panel, pick 10 minutes, then Custom range. "Each device has an AVL tree archive of about 8,700 readings keyed by time. A range query only visits the subtrees that could match, so it takes O(log n plus k) instead of reading everything." Show nodes visited against archive size, then run the benchmark.

**8. Code (4:15)**
Show `AvlTree.cs` range query, `MeshGraph.cs` Dijkstra, and `LinkCost.cs` operators in a few seconds each. Mention the 25 new unit tests and the green GitHub Actions run.

**9. Close (4:50)**
"That covers the tree, the graph algorithms, self-healing routing and the topology page. Thanks for watching."

After uploading, replace the `VIDEO LINK` in README.md.
