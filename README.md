# Neovis_Trade_Illustration
An explanation of my Grand Strategy Game's trade system

A hub-based trade system for a province-level grand strategy game I've been building using Unity and C#. Every province finds its local trade center using a price equation, with both distances and taxes being accounted for, and then sell to and buy from that trade center. Trade centers balance demand among themselves, selling their excess and buying what their client provinces demanded, with distance and taxes still being considered. This system allows for players to levy taxes on their provinces or on strategic choke points, and smart path recalculation will allow for the provinces to recalculate their most strategic move, making some taxes just too high to put up with, thus forcing a balance on taxes to the player.

<img width="512" height="287" alt="Screenshot 2026-09-16 210633" src="https://github.com/user-attachments/assets/16a40448-cc80-4edd-98a1-13abd9a16e90" />
