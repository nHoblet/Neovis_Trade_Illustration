# Neovis_Trade_Illustration
An explanation of my Grand Strategy Game's trade system

A hub-based trade system for a province-level grand strategy game I've been building using Unity and C#. Every province finds its local trade center using a price equation, with both distances and taxes being accounted for, and then sell to and buy from that trade center. Trade centers balance demand among themselves, selling their excess and buying what their client provinces demanded, with distance and taxes still being considered. This system allows for players to levy taxes on their provinces or on strategic choke points, and smart path recalculation will allow for the provinces to recalculate their most strategic move, making some taxes just too high to put up with, thus forcing a balance on taxes to the player.

<img width="512" height="287" alt="Screenshot 2026-09-16 210633" src="https://github.com/user-attachments/assets/16a40448-cc80-4edd-98a1-13abd9a16e90" />

## Resources and regional imbalance

Each province is assigned a primary resource (and sometimes a secondary one) that it can produce, such as iron, timber, food, or silk. A province only produces that resource if it has the matching extraction building (a Mine, Lumber Camp, or Farm, for example), and output scales with the building's level. Every province also consumes goods: food for its population, upkeep for its buildings, luxury goods for its nobles and merchants, and materials for construction.

Because the map assigns resources unevenly, some provinces produce far more of a good than they use, while others produce none of what they need. Each month, a province's production minus its consumption becomes either an export order (surplus) or an import order (shortfall). Those orders are the demand and supply that the trade system balances.
<img width="515" height="287" alt="Screenshot 2026-09-16 211645" src="https://github.com/user-attachments/assets/2a25e112-6d57-460c-8a8e-9b8929306c4f" />


## **Trade center map**
<img width="513" height="290" alt="Screenshot 2026-09-16 212015" src="https://github.com/user-attachments/assets/2448a296-4fb0-45b8-9572-89025a0ee096" />
_This is the overall trade center map. Where all resources are bought from or sold to_

<img width="467" height="265" alt="image" src="https://github.com/user-attachments/assets/cc08a462-d8d8-4861-874c-ff439ccc6aef" />
_Here I have a province selected to show the provinces it buys and sells from. As you can tell, this province is a trade center._
