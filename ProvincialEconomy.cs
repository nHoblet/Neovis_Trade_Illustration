using UnityEngine;
using System.Collections.Generic;
using System.Linq;


public class ProvincialEconomy : MonoBehaviour
{
    [Header("Script Reference")]
    public MapData mapData;
    public Buildings buildings;
    public TradeSystem tradeSystem;
    public GameData gameData;
    [SerializeField] private TimeController timeController;

    [Header("Trade Hub / Speculative Conversion Settings")]
    [Tooltip("If true, conversion buildings flagged canImportFeedstock may be proposed with zero local production, betting on future imports.")]
    public bool allowSpeculativeConversionBuildings = true;

    [Tooltip("Premium multiplier over current local price used to pessimistically estimate the cost of importing feedstock (accounts for distance/shipping risk vs. a probe that's actually local).")]
    public float speculativeImportPremium = 1.4f;

    [Tooltip("Max number of import-fed converters of the same resource a single province may run at once. Keeps one bad bet from snowballing.")]
    public int maxSpeculativeConvertersPerProvince = 1;
    public List<HeldTradeOrder> heldConstructionOrders = new List<HeldTradeOrder>();


    [Header("Values")]
    public float producedPerLevel;
    int monthsInYear = 12;
    [SerializeField] int maxConcurrentProjectsPerProvince = 2;

    [Header("Trade Center Assignment")]
    [SerializeField] private Texture2D tradeCentersMap;
    public Color tradeCenterColor;

    // Single source of truth for "which building extracts which resource".
    // Used by GetBuildingType, ExpectedBuildingTypeForResource, and
    // InitializeResourceProductionSingle, which previously each had their
    // own copy of this mapping (and disagreed on Oil — this version includes it).
    public static readonly Dictionary<ResourceType, string> extractionBuildingByResource = new Dictionary<ResourceType, string>
    {
        { ResourceType.Iron, "Mine" },
        { ResourceType.Copper, "Mine" },
        { ResourceType.Gold, "Mine" },
        { ResourceType.Coal, "Mine" },
        { ResourceType.Timber, "Lumber Camp" },
        { ResourceType.Stone, "Quarry" },
        { ResourceType.Oil, "Oil Rig" },
        { ResourceType.Food, "Farm" },
        { ResourceType.Salt, "Salt Works" },
        { ResourceType.Mustard, "Mustard Fields" },
        { ResourceType.Saffron, "Saffron Fields" },
        { ResourceType.Ginger, "Ginger Plantation" },
        { ResourceType.Cinnamon, "Cinnamon Grove" },
        { ResourceType.Cardamom, "Cardamom Plantation" },
        { ResourceType.Cloves, "Clove Plantation" },
        { ResourceType.Nutmeg, "Nutmeg Grove" },
        { ResourceType.Gems, "Gem Mine" },
        { ResourceType.OliveOil, "Olive Grove" },
        { ResourceType.BlackPepper, "Pepper Plantation" },
        { ResourceType.Indigo, "Indigo Plantation" },
        { ResourceType.Cochineal, "Cochineal Plantation" },
        { ResourceType.TyrianPurple, "Murex Fishery" },
        { ResourceType.Madder, "Madder Farm" },
        { ResourceType.Silk, "Silkworm Estate" },
        { ResourceType.Cotton, "Cotton Gin" },
        { ResourceType.Wool, "Sheep Ranch" },
        { ResourceType.Silver, "Silver Mine" },
        { ResourceType.Ivory, "Ivory Hunting Lodge" },
        { ResourceType.Sugar, "Sugar Mill" },
        { ResourceType.Tea, "Tea Estate" },
        { ResourceType.Coffee, "Coffee Plantation" },
        { ResourceType.Wine, "Vineyard" },
        { ResourceType.Cocoa, "Cocoa Plantation" },
        { ResourceType.Incense, "Frankincense Plantation" },
        { ResourceType.Tobacco, "Tobacco Plantation" },
        { ResourceType.Porcelain, "Porcelain Kiln" },
        { ResourceType.Furs, "Trapper's Lodge" },
        { ResourceType.Paper, "Paper Mill" },
    };

    static readonly Dictionary<ResourceType, string> conversionBuildingByResource = new Dictionary<ResourceType, string>
    {
        { ResourceType.Iron, "Metal Workshop" },
        { ResourceType.Copper, "Toolshop" },
        { ResourceType.Gold, "Mint" },
        { ResourceType.Coal, "Furnace" },
        { ResourceType.Timber, "Sawmill" },
        { ResourceType.Stone, "Masonry" },
        { ResourceType.Oil, "Refinery" },
        { ResourceType.Food, "Mill" },
    };


    public void CalledProvEconomyStart()
    {
        monthsInYear = timeController.monthNames.Count;

        mapData.InitializeNationsAtStart();

        StartingGift();
        InitializeBuildings();
        InitializeResourceProduction();
        InitializeResourceConsumption();

    }

    public void InitializePopulationGroups()
    {

    }


    public void StartingGift()
    {
        //Called at the very start of the game to set the resources provinces initially needed
        for (int i = 0; i < gameData.localCopy.provinceList.Count; i++)
        {
            var province = gameData.localCopy.provinceList[i];
            if (province.ownerInt == -1) continue;

            province.tradeReciept.GetStock(ResourceType.Iron).amount = 25;
            province.tradeReciept.GetStock(ResourceType.Copper).amount = 0;
            province.tradeReciept.GetStock(ResourceType.Gold).amount = 0;
            province.tradeReciept.GetStock(ResourceType.Timber).amount = 50;
            province.tradeReciept.GetStock(ResourceType.Stone).amount = 50;
            province.tradeReciept.GetStock(ResourceType.Coal).amount = 0;
            province.tradeReciept.GetStock(ResourceType.Oil).amount = 0;

            province.tradeReciept.GetStock(ResourceType.Food).amount = 100; //1 year of food

        }
    }

    public float PopulationHunger(int i)
    {
        //Called once a month for provinces to eat their food. Unfed people starve
        ProvinceList province = gameData.localCopy.provinceList[i];
        float totalFoodNeeded = 0f;
        float totalProvincePopulation = 0f;
        var foodStock = province.tradeReciept.GetStock(ResourceType.Food);

        foreach (PopulationGroup group in province.populationGroups)
        {
            float population = group.population;
            float foodNeeded = population / 100000f;  // One person eats 0.00001 food per month
            totalFoodNeeded += foodNeeded;
            float foodAvailable = foodStock.amount;

            // How much of the need is fulfilled
            float foodFulfilled = Mathf.Min(foodNeeded, foodAvailable);

            // How much is unmet (starvation)
            float foodUnmet = Mathf.Max(0, foodNeeded - foodFulfilled);

            // Update province's food stock
            foodStock.amount = Mathf.Max(0, foodAvailable - foodFulfilled);

            // Example: apply starvation effects
            if (foodUnmet > 0)
            {
                // Fraction of population that starves
                float starvationRate = foodUnmet / foodNeeded;
                float deaths = population * starvationRate * 0.5f; //Quite brutal, but percentage can be trimmed
                group.population = Mathf.Max(0, population - deaths);
                NationList nation = gameData.localCopy.nationList[province.ownerInt];
            }
            else
            {
                Culture thisCulture = mapData.cultures.FirstOrDefault(c => c.cultureName == group.culture);
                if (thisCulture == null)
                {
                    continue;
                }
                float yearlyPopGrowth = thisCulture.reproductionRate; //Reproduction based on group
                int numberOfMonths = timeController.monthNames.Count;
                group.population *= 1 + (yearlyPopGrowth / numberOfMonths);
            }

            totalProvincePopulation += group.population;
        }

        // Update the overall province population as sum of all groups
        province.population = totalProvincePopulation;

        // Same field previously known as province.foodConsumed — moved onto the
        // Food stock entry. Note this still overwrites whatever building upkeep
        // consumption InitializeResourceConsumptionSingle put there earlier, same
        // as the original code did.
        foodStock.consumed = totalFoodNeeded;

        gameData.localCopy.provinceList[i] = province;

        // Update primary religion based on population groups
        SetPrimaryReligionForProvince(i);

        return totalFoodNeeded;
    }

    public void SetPrimaryReligionForProvince(int i)
    {
        // Determine the primary religion based on largest population
        ProvinceList province = gameData.localCopy.provinceList[i];
        Dictionary<string, float> populationByReligion = new Dictionary<string, float>();

        province.populationGroups ??= new List<PopulationGroup>();

        if (province.populationGroups.Count == 0)
        {
            province.primaryReligion = "None";
            gameData.localCopy.provinceList[i] = province;
            return;
        }

        foreach (PopulationGroup group in province.populationGroups)
        {
            if (!populationByReligion.ContainsKey(group.religion))
            {
                populationByReligion[group.religion] = 0f;
            }
            populationByReligion[group.religion] += group.population;
        }

        // Set the province's primary religion to the largest religion by population
        string largestReligion = "";
        float largestPopulation = 0f;
        foreach (var religionEntry in populationByReligion)
        {
            if (religionEntry.Value > largestPopulation)
            {
                largestPopulation = religionEntry.Value;
                largestReligion = religionEntry.Key;
            }
        }

        if (!string.IsNullOrEmpty(largestReligion))
        {
            province.primaryReligion = largestReligion;
            gameData.localCopy.provinceList[i] = province;
        }
    }

    public void InitializeResourceProductionSingle(int i)
    {
        ProvinceList province = gameData.localCopy.provinceList[i];

        foreach (var s in province.tradeReciept.stock)
            s.produced = 0;

        string normalized = province.resourceProduced.Replace(" ", "");
        bool success = System.Enum.TryParse<ResourceType>(normalized, true, out var producedType);

        if (!extractionBuildingByResource.TryGetValue(producedType, out var requiredBuildingType))
            return;

        var producedStock = province.tradeReciept.GetStock(producedType);

        foreach (var building in province.buildings)
        {
            if (building.buildingType == requiredBuildingType)
                producedStock.produced += building.buildingLevel * producedPerLevel;
        }
    }

    public void InitializeResourceProductionSecondary(int i)
    {
        ProvinceList province = gameData.localCopy.provinceList[i];

        string normalized = province.resourceProducedSecondary.Replace(" ", "");
        bool success = System.Enum.TryParse<ResourceType>(
            normalized, 
            true, 
            out var producedType
        );

        if (!success)
            return;

        if (!extractionBuildingByResource.TryGetValue(producedType, out var requiredBuildingType))
            return;

        var producedStock = province.tradeReciept.GetStock(producedType);

        foreach (var building in province.buildings)
        {
            if (building.buildingType == requiredBuildingType)
                producedStock.produced += building.buildingLevel * producedPerLevel;
        }
    }

    public void InitializeResourceProduction()
    {
        //Set the initial resource production, then when it changes we'll write to that integer
        //Makes monthly province production compute easy
        for (int i = 0; i < gameData.localCopy.provinceList.Count; i++)
        {
            InitializeResourceProductionSingle(i);
            InitializeResourceProductionSecondary(i);
        }

    }

    public void InitializeBuildings()
    {
        for (int i = 0; i < gameData.localCopy.provinceList.Count; i++)
        {
            var province = gameData.localCopy.provinceList[i];
            province.buildings.Clear();

            int owner = province.ownerInt;
            if (owner != -1)
            {
                int devScore = mapData.nationsAtStart[owner].developmentScore;
                string resourceProduced = province.resourceProduced;

                int buildingLevel = mapData.GetBuildingLevel(devScore, 5);
                string buildingType = GetBuildingType(resourceProduced);

                var nation = gameData.localCopy.nationList[owner];

                if (buildingLevel > 0 && buildingType != "0" && buildingType != "")
                {
                    ProvinceBuilding newPB = new ProvinceBuilding();
                    newPB.buildingType = buildingType;
                    newPB.buildingLevel = buildingLevel;
                    newPB.activeLevels = buildingLevel;
                    gameData.localCopy.provinceList[i].buildings.Add(newPB);

                }

                string secondaryResource = province.resourceProducedSecondary;
                if(secondaryResource != "" && secondaryResource != null){
                    string buildingType2 = GetBuildingType(secondaryResource);

                    if (buildingLevel > 0 && buildingType != "0" && buildingType != "")
                    {
                        ProvinceBuilding newPB2 = new ProvinceBuilding();
                        newPB2.buildingType = buildingType2;
                        newPB2.buildingLevel = buildingLevel;
                        newPB2.activeLevels = buildingLevel;
                        gameData.localCopy.provinceList[i].buildings.Add(newPB2);

                    }
                }
            }

            int x = Mathf.RoundToInt(province.position.x);
            int y = Mathf.RoundToInt(province.position.y);
            Color texColor = tradeCentersMap.GetPixel(x, y);
            if (mapData.colDist(tradeCenterColor, texColor) < 0.15f)
            {
                ProvinceBuilding newPB2 = new ProvinceBuilding();
                newPB2.buildingType = "Trade Center";
                newPB2.buildingLevel = 1;
                newPB2.activeLevels = 1;
                gameData.localCopy.provinceList[i].buildings.Add(newPB2);
            }
            gameData.localCopy.provinceList[i] = province;
        }

    }

    string GetConversionBuildingType(string resourceProduced)
    {
        return ExpectedConversionBuildingTypeForResource(resourceProduced) ?? "";
    }

    string GetBuildingType(string resourceProduced)
    {
        return ExpectedBuildingTypeForResource(resourceProduced) ?? "";
    }

    public void InitializeResourceConsumptionSingle(int i)
    {
        ProvinceList province = gameData.localCopy.provinceList[i];

        foreach (var s in province.tradeReciept.stock)
            s.consumed = 0;

        foreach (var provinceBuilding in province.buildings)
        {
            var building = buildings.GetBuildingFromProvinceBuilding(provinceBuilding.buildingType);

            if (building == null)
                continue;
            if (building.resourceUpkeep == null)
                continue;

            foreach (var upkeepCost in building.resourceUpkeep)
            {
                float add = upkeepCost.amount * provinceBuilding.activeLevels / monthsInYear;

                if (upkeepCost.resourceName == "Nock")
                {
                    ResourceStock nocksStock = province.stock.Find(s => s.type == ResourceType.Nocks);
                    if (nocksStock != null)
                    {
                        nocksStock.consumed += add;
                    }
                    else
                    {
                        province.stock.Add(new ResourceStock
                        {
                            type = ResourceType.Nocks,
                            amount = 0,
                            produced = 0,
                            consumed = add
                        });
                    }                    
                    continue;
                }
                
                string normalized = province.resourceProduced.Replace(" ", "");
                bool success = System.Enum.TryParse<ResourceType>(normalized, true, out var type);

                if (success)
                {
                    province.tradeReciept.GetStock(type).consumed += add;
                }
            }

        }
    }

    public void InitializeResourceConsumption()
    {
        for (int i = 0; i < gameData.localCopy.provinceList.Count; i++)
        {
            InitializeResourceConsumptionSingle(i);
        }
    }

    public void ResourceChange()
    {
        //Reset lists
        gameData.localCopy.exportOrders.Clear();
        gameData.localCopy.importOrders.Clear();

        // --- NEW: Push held-over construction orders into the live system ---
        if (heldConstructionOrders != null && heldConstructionOrders.Count > 0)
        {
            foreach (var order in heldConstructionOrders)
            {
                tradeSystem.PlaceTradeOrder(
                    order.provinceIndex, 
                    order.goodIndex, 
                    order.amount, 
                    order.nationIndex, 
                    order.orderType
                );
            }
            // Clear them out now that they've been officially queued for this tick
        }
        // --------------------------------------------------------------------


        //Reset texts
        for (int u = 0; u < gameData.localCopy.nationList.Count; u++)
        {
            gameData.localCopy.nationList[u].lastTradeExpense = 0;
            gameData.localCopy.nationList[u].lastTradeRevenue = 0;
        }

        for (int i = 0; i < gameData.localCopy.provinceList.Count; i++)
        {
            ProvinceList province = gameData.localCopy.provinceList[i];
            int owner = -1;
            int.TryParse(province.owner, out owner);
            if (owner == -1)
                continue;
            
            //Import all the luxury goods we can befitting the amount of merchants and nobility in this province
            ImportLuxuryTradeGoods(i);


            // Manpower increase
            if (gameData.localCopy.nationList[owner].manpower < gameData.localCopy.nationList[owner].maxManpower)
            {
                foreach (ProvinceBuilding pB in province.buildings)
                {
                    /*if (pB.buildingType == "Recruitment Post")
                    {
                        float increase = Mathf.Min(province.population / 20, pB.buildingLevel * 50);

                        // Add to manpower (capped at maxManpower)
                        gameData.localCopy.nationList[owner].manpower = Mathf.Min(
                            gameData.localCopy.nationList[owner].manpower + increase,
                            gameData.localCopy.nationList[owner].maxManpower
                        );

                        // Subtract the same amount from province population
                        province.population = Mathf.Max(province.population - increase, 0);
                    }*/
                }
            }

            //Reset demanded / expense
            province.tradeReciept.ResetDemanded();
            province.tradeReciept.ResetExpense();

            if (heldConstructionOrders != null && heldConstructionOrders.Count > 0)
            {
                foreach (var order in heldConstructionOrders)
                {
                    ResourceType type = (ResourceType)order.goodIndex;
                    var receipt = gameData.localCopy.provinceList[order.provinceIndex].tradeReciept.Get(type);

                    receipt.needed += order.amount;
                    receipt.demanded += order.amount;
                }
            }

            //Production
            foreach (var s in province.tradeReciept.stock)
                s.amount += s.produced;

            //Population upkeep
            province.tradeReciept.GetStock(ResourceType.Food).amount += PopulationHunger(i);

            //Consumption
            foreach (var s in province.tradeReciept.stock)
                s.amount -= s.consumed;

            //Add locally used resources so that those buildings don't get shut down just cuz they ain't export
            foreach (var s in province.tradeReciept.stock)
            {
                float localUsed = Mathf.Min(s.produced, s.consumed);
                float income = localUsed * tradeSystem.GetResourcePriceForProvinceandTradeGood(i, (int)s.type);

                var receipt = province.tradeReciept.Get(s.type);
                receipt.expense += income;
                receipt.revenue += income;

                gameData.localCopy.nationList[owner].lastTradeExpense += income;
                gameData.localCopy.nationList[owner].lastTradeRevenue += income;
            }

            //Nation money
            province.GetOrAddStock(ResourceType.Nocks).amount -= province.GetOrAddStock(ResourceType.Nocks).consumed;

            gameData.localCopy.provinceList[i] = province;

            //Queue building resource needs
            var queueCost = new Dictionary<ResourceType, float>();

            if (province.buildingQueue != null && province.buildingQueue.Count > 0)
            {
                foreach (BuildingUnderConstruction queuedBuilding in province.buildingQueue)
                {
                    Building related = buildings.buildingsList.Find(b => b.name == queuedBuilding.buildingType);
                    if (related == null) continue;

                    foreach (HandyResource r in related.resourceCost)
                    {
                        string normalized = province.resourceProduced.Replace(" ", "");
                        bool success = System.Enum.TryParse<ResourceType>(normalized, true, out var type);

                        if (success)
                        {
                            queueCost.TryGetValue(type, out var current);
                            queueCost[type] = current + r.amount;
                        }
                    }
                }
            }

            //Convert resources
            RunResourceConversions();


            //Place trade orders
            foreach (var s in province.tradeReciept.stock)
            {
                queueCost.TryGetValue(s.type, out var forQueue);
                float balance = s.consumed + forQueue - s.produced + GetConversionInputDemand(province, s.type.ToString());
                HandleTrade(i, (int)s.type, balance, owner);
            }
        }
        heldConstructionOrders.Clear();

    }

    public void ImportLuxuryTradeGoods(int provinceIdx)
    {
        ProvinceList province = gameData.localCopy.provinceList[provinceIdx];
        float amountOfNobles = province.populationGroups
            .Where(group => group.socialGroup == "Nobility")
            .Sum(group => group.population);
        
        float amountOfMerchants = province.populationGroups
            .Where(group => group.socialGroup == "Merchants")
            .Sum(group => group.population);

        float amountPerNoble = 0.00001f; // 1 per ten thousand
        float amountPerMerchant = 0.00001f; // 1 per ten thousand 

        float amountToImport = (amountOfNobles * amountPerNoble) + (amountOfMerchants * amountPerMerchant);

        for(int i = 0; i < 30; i++){
            
        tradeSystem.PlaceTradeOrder(
            provinceIdx, 
            i+8,//the good index. The index in secondary goods plus the amount of primary (non luxury) goods 
            amountToImport, 
            province.ownerInt, 
            "Import"
        );

        }

        Debug.Log("Called order to import luxury goods");

    }

    public void RunResourceConversions()
    {
        for (int i = 0; i < gameData.localCopy.provinceList.Count; i++)
        {
            ProvinceList province = gameData.localCopy.provinceList[i];
            int owner = province.ownerInt;
            if (owner == -1 || province.buildings == null) continue;

            foreach (var provinceBuilding in province.buildings)
            {
                var building = buildings.GetBuildingFromProvinceBuilding(provinceBuilding.buildingType);
                if (building == null || string.IsNullOrEmpty(building.convertsFrom)) continue;

                float available = GetProvinceResource(province, building.convertsFrom);
                float capacity = provinceBuilding.activeLevels * building.conversionRate; // max nocks/consumption this building can do
                float wantsToConsume = provinceBuilding.activeLevels * producedPerLevel; // reuse your existing per-level rate for input amount
                float actualConsumed = Mathf.Min(available, wantsToConsume);

                if (actualConsumed <= 0f) continue;

                SubtractProvinceResource(province, building.convertsFrom, actualConsumed);

                float nocksGenerated = actualConsumed * building.conversionRate;
                gameData.localCopy.nationList[owner].nocks += nocksGenerated;

                // Track it for HandleReciepts profitability + UI, same pattern as trade receipts
                province.tradeReciept.conversionRevenue += nocksGenerated;
            }

            gameData.localCopy.provinceList[i] = province;
        }
    }


    void HandleTrade(int provinceIndex, int goodIndex, float balance, int nation)
    {
        ResourceType type = (ResourceType)goodIndex;
        var receipt = gameData.localCopy.provinceList[provinceIndex].tradeReciept.Get(type);

        if (balance > 0)
        {
            tradeSystem.PlaceTradeOrder(provinceIndex, goodIndex, balance, nation, "Import");
            receipt.needed += balance;
            receipt.demanded += balance;

            Debug.Log("Importing " + goodIndex.ToString());
        }
        else if (balance < 0)
        {
            tradeSystem.PlaceTradeOrder(provinceIndex, goodIndex, -balance, nation, "Export");
                        Debug.Log("Exporting " + goodIndex.ToString());

        }else if(balance == 0)
        {
            Debug.Log("balance for" + goodIndex.ToString() + " is zero");
        }
    }


    //Handle the aftermath of trading
    public void HandleReciepts()
    {
        // Trade-hub-demand driven construction: build the nation's list of
        // profitable prospects (both existing-building upgrades and brand new
        // builds), then commit budget/imports to the best ones. No separate
        // per-building profitability pass — QueueProspectives is the single
        // source of "should we build/upgrade this?" decisions.
        QueueProspectives();
        ManageConstructionQueue();
    }

    public void UIQueueConstruction(string buildingType)
    {
        QueuePlayerConstruction(mapData.openedProvince, buildingType);
    }

    //AI - Call from buildings etc. to add a construction
    public void QueuePlayerConstruction(int provinceIdx, string buildingType)
    {
        var nation = gameData.localCopy.nationList[gameData.localCopy.provinceList[provinceIdx].ownerInt];
        nation.playerQueuedRequests ??= new List<ConstructionRequest>();

        if (nation.playerQueuedRequests.Any(r => r.provinceIdx == provinceIdx && r.buildingType == buildingType))
            return; // already queued, don't duplicate

        nation.playerQueuedRequests.Add(new ConstructionRequest
        {
            provinceIdx = provinceIdx,
            buildingType = buildingType,
            source = ConstructionSource.Player,
            priorityScore = float.MaxValue // player intent always outranks AI guesses
        });
    }



    /* Find which buildings for our nation would be a profitable investment based on the trade centers we deal with.
        i.e. If food is over-demanded and under-supplied, and we can produce it, let's build it up.
        If food is over-supplied and under-demanded, we can build a conversion building to turn it into nocks
    */
public void QueueProspectives()
{
    for(int provIdx = 0; provIdx < gameData.localCopy.provinceList.Count; provIdx++)
    {
        var province = gameData.localCopy.provinceList[provIdx];
        if(province.ownerInt == -1) continue;
        NationList nation = gameData.localCopy.nationList[province.ownerInt];
        nation.autoConstructionRequests = new List<ConstructionRequest>(); // was prospectiveConstructions

        for(int tradeGoodIdx = 0; tradeGoodIdx < buildings.resourceNames.Count; tradeGoodIdx++)
        {
            EvaluateTradeGoodProspective(provIdx, province, nation, tradeGoodIdx, buildings.resourceNames[tradeGoodIdx]);
        }

        int editedIdx = buildings.resourceNames.Count;
        foreach(SecondaryTradeGoods seconaryTradeGood in mapData.secondaryTradeGoods)
        {
            EvaluateTradeGoodProspective(provIdx, province, nation, editedIdx, seconaryTradeGood.name);
            editedIdx++;
        }
    }   
}

    private void EvaluateTradeGoodProspective(int provIdx, ProvinceList province, NationList nation, int tradeGoodIdx, string resourceName)
    {
        if(!province.tradeClientData.sellToTCForGood.ContainsKey(tradeGoodIdx)) return;

        TCDataClass tCDataClass = province.tradeClientData.sellToTCForGood[tradeGoodIdx];
        int sellToIdx = tCDataClass.destinationTCIdx; 
        ProvinceList tradeCenterProvince = gameData.localCopy.provinceList[sellToIdx];

        /*
        Did purchase requests exceed stock? 
            - If we have that resource type, build it up

        Was there too MUCH stock?
            - We can build up a conversion building
            But then, how do we deal with everyone else doing the same thing?
            We can randomize if we build it this tick or not
        
        Do we still have budget left?
            - We could build up roads
            - We could improve our trade centers

        */

        var tcData = tradeCenterProvince.tradeCenterData;
        
        if(!tcData.inititalStockRecieved.ContainsKey(tradeGoodIdx)) return;

        float initialAmount = tcData.inititalStockRecieved[tradeGoodIdx];
        float remainingAmount = tcData.storedTradeCenterGoods[tradeGoodIdx]; //100 remaining and 200 initial = 0.5
        float importerDemand = tcData.stockDemanded[tradeGoodIdx];

        if(resourceName == province.resourceProduced && importerDemand > initialAmount)
        {
            nation.prospectiveConstructions??= new List<ProspectiveConstruction>();

            //Purchase requests exceed the stock for a resource that we can produce
            ProspectiveConstruction newProspective = new ProspectiveConstruction();
            newProspective.provinceIdx = provIdx;
            newProspective.buildingType = GetBuildingType(province.resourceProduced);
            newProspective.estimatedShortTermProfit = tcData.tradeCenterPricing[tradeGoodIdx]-tCDataClass.distanceCost; //price going for in market - transport cost
            nation.prospectiveConstructions.Add(newProspective);
        }

        //Now look at building a conversion building

        string conversionBuildingType = GetConversionBuildingType(province.resourceProduced);
        if(conversionBuildingType == "") return; //Don't try to add a conversion building if one doesn't exist for this.


        float excessThreshold = 1.10f; //More than 10% and 2 units of excess stock over demand
        if(initialAmount*excessThreshold + 2 > importerDemand)
        {
            nation.prospectiveConstructions??= new List<ProspectiveConstruction>();

            ProspectiveConstruction newProspective = new ProspectiveConstruction();
            newProspective.provinceIdx = provIdx;
            newProspective.buildingType = conversionBuildingType;
            newProspective.estimatedShortTermProfit = GetConversionPriceForGood(province.resourceProduced)-(tcData.tradeCenterPricing[tradeGoodIdx]+tCDataClass.distanceCost); // conversion per unit - (cost of importing + distance cost)
            nation.prospectiveConstructions.Add(newProspective);
        }
    }


    float GetConversionPriceForGood(string resourceType)
    {
        foreach(Building building in buildings.buildingsList)
        {
            if(building.isConversionBuilding && building.convertsFrom == resourceType)
                return building.conversionRate;
        } 
        return 0;
    }

    public void ManageConstructionQueue()
    {
        foreach (NationList nation in gameData.localCopy.nationList)
        {
            var allRequests = new List<ConstructionRequest>();
            if (nation.playerQueuedRequests != null) allRequests.AddRange(nation.playerQueuedRequests);
            if (nation.autoConstructionRequests != null) allRequests.AddRange(nation.autoConstructionRequests);

            var ordered = allRequests
                .OrderByDescending(r => r.source == ConstructionSource.Player)
                .ThenByDescending(r => r.priorityScore)
                .ToList();

            foreach (var req in ordered)
            {
                if (string.IsNullOrEmpty(req.buildingType)) continue;
                if (req.source != ConstructionSource.Player && req.priorityScore <= 0f) continue;

                var province = gameData.localCopy.provinceList[req.provinceIdx];
                Building building = buildings.buildingsList.Find(b => b.name == req.buildingType);
                if (building == null) continue;

                int existingIdx = province.buildings?.FindIndex(b => b.buildingType == req.buildingType) ?? -1;
                bool isUpgrade = existingIdx >= 0;

                ProvinceBuilding targetBuilding = isUpgrade
                    ? province.buildings[existingIdx]
                    : new ProvinceBuilding { buildingType = req.buildingType, buildingLevel = 0, activeLevels = 0 };

                TryQueueConstruction(req.provinceIdx, targetBuilding, building, isNewBuilding: !isUpgrade);
            }

            // player requests that got started (or hit a same-type-active gate) shouldn't keep re-firing every tick
            nation.playerQueuedRequests?.RemoveAll(r =>
                gameData.localCopy.buildingQueue.Any(b => b.province == r.provinceIdx && b.buildingType == r.buildingType) ||
                gameData.localCopy.provinceList[r.provinceIdx].buildingQueue.Any(p => p.buildingType == r.buildingType));
        }
    }

    void TryQueueConstruction(int i, ProvinceBuilding provinceBuilding, Building building, bool isNewBuilding = false)
    {
        // --- Find owning nation ---
        int ownerIndex = 0;
        if (!int.TryParse(gameData.localCopy.provinceList[i].owner, out ownerIndex))
        {
            Debug.LogError($"Province {i} has non-numeric owner '{gameData.localCopy.provinceList[i].owner}'; defaulting to nation 0.");
        }

        var provinceBuildQueue = gameData.localCopy.provinceList[i].buildingQueue;

        // A project already fully resourced and under construction anywhere in this
        // province blocks a second one from starting. A pending (still-importing)
        // project for a DIFFERENT building type also blocks it — one construction
        // slot per province. A pending project for THIS SAME building type is fine:
        // that's just this same project continuing across ticks, picked up below
        // instead of bailing out on it (the old blanket check here meant a project
        // that started saving up could never be revisited to see if its imports
        // had arrived).
        bool sameTypeActive = gameData.localCopy.buildingQueue
            .Any(vbUC => vbUC.province == i && vbUC.buildingType == provinceBuilding.buildingType);
        if (sameTypeActive) return; // already building/upgrading exactly this

        int concurrentCount = gameData.localCopy.buildingQueue.Count(vbUC => vbUC.province == i)
                            + provinceBuildQueue.Count(p => p.buildingType != provinceBuilding.buildingType);
        if (concurrentCount >= maxConcurrentProjectsPerProvince) return;

        NationList owningNation = gameData.localCopy.nationList[ownerIndex];

        if (owningNation.constructionMode == ConstructionMode.PlayerQueued)
        {
            // Only proceed if player has queued this province
            bool isPlayerQueued = owningNation.pendingConstructionQueue
                .Any(p => p.province == i && p.buildingType == provinceBuilding.buildingType);
            if (!isPlayerQueued)
                return;
        }

        // Snapshot of what this build needs, per resource — used for the
        // UI-facing resourcesRequired/resourcesCommitted tracking below.
        Dictionary<ResourceType, float> requiredSnapshot = new Dictionary<ResourceType, float>();
        foreach (HandyResource hr in building.resourceCost)
        {
                string normalized = hr.resourceName.Replace(" ", "");
                bool success = System.Enum.TryParse<ResourceType>(normalized, true, out var snapType);

                if (success)
                requiredSnapshot[snapType] = hr.amount;
        }

        var pendingEntry = provinceBuildQueue.FirstOrDefault(p => p.buildingType == provinceBuilding.buildingType && p.province == i);

        // --- Has everything actually arrived? Every resource requirement is
        // checked against the province's real stock-on-hand — whether it was
        // there from the start, or delivered by an import order placed on an
        // earlier tick via the trade system. If so, the importing phase is done
        // and construction can start right now.
        bool everythingArrived = true;
        foreach (HandyResource handyResource in building.resourceCost)
        {
            float available = GetProvinceResource(gameData.localCopy.provinceList[i], handyResource.resourceName);
            if (available < handyResource.amount)
            {
                everythingArrived = false;
                break;
            }
        }

        if (everythingArrived)
        {
            foreach (HandyResource handyResource in building.resourceCost)
            {
                SubtractProvinceResource(gameData.localCopy.provinceList[i], handyResource.resourceName, handyResource.amount);
            }

            var newBuild = new BuildingUnderConstruction
            {
                nation = ownerIndex,
                buildingType = provinceBuilding.buildingType,
                previouslyExisted = !isNewBuilding,
                daysRemaining = building.daysToConstruct,
                province = i,
                resourcesCommittedValue = pendingEntry != null ? pendingEntry.resourcesCommittedValue : 0,
                resourcesRequired = new Dictionary<ResourceType, float>(requiredSnapshot),
                resourcesCommitted = new Dictionary<ResourceType, float>(requiredSnapshot) // fully resourced by definition
            };

            if (pendingEntry != null)
                provinceBuildQueue.Remove(pendingEntry);

            gameData.localCopy.buildingQueue.Add(newBuild);
            
            owningNation.pendingConstructionQueue ??= new List<BuildingUnderConstruction>();
            owningNation.pendingConstructionQueue.RemoveAll(p =>
                p.province == i && p.buildingType == provinceBuilding.buildingType);

            return;
        }

        // --- Still missing something. Figure out how much budget is free this
        // tick to place NEW import orders — orders placed on earlier ticks resolve
        // through the normal trade system on their own and just show up as
        // increased "available" stock above; this only covers newly-identified
        // shortfall.
        float remainingBudget = owningNation.constructionBudget - owningNation.constructionSpentThisMonth;
        if (remainingBudget <= 0f)
            return; // no budget left this month — the shortfall just waits and gets re-evaluated next tick

        float availableForImports = Mathf.Max(0f, remainingBudget);

        float spentThisTick = 0f;
        var orderedThisTick = new Dictionary<ResourceType, float>();

        foreach (HandyResource handyResource in building.resourceCost)
        {
            if (availableForImports <= 0f) break;
            string normalized = handyResource.resourceName.Replace(" ", "");
            bool success = System.Enum.TryParse<ResourceType>(normalized, true, out var type);

            if (!success) continue;

            float available = GetProvinceResource(gameData.localCopy.provinceList[i], handyResource.resourceName);
            float shortfall = Mathf.Max(0f, handyResource.amount - available);
            if (shortfall <= 0f) continue;

            int goodIndex = buildings.resourceNames.IndexOf(handyResource.resourceName);
            float price = tradeSystem.GetResourcePriceForProvinceandTradeGood(i, goodIndex);
            if (price <= 0f) continue;

            float canAffordUnits = Mathf.Floor(availableForImports / price);
            float unitsToOrder = Mathf.Min(canAffordUnits, shortfall);
            if (unitsToOrder <= 0f) continue;

            // Place a real import order — the goods arrive through the normal
            // trade resolution on a later tick, same as any other import demand
            // (see ), rather than being materialized instantly here.
                        
            // --- WITH THIS ---
            heldConstructionOrders.Add(new HeldTradeOrder {
                provinceIndex = i,
                goodIndex = goodIndex,
                amount = unitsToOrder,
                nationIndex = ownerIndex,
                orderType = "Import"
            });            
            



            float spent = unitsToOrder * price;
            availableForImports -= spent;
            spentThisTick += spent;

            orderedThisTick[type] = orderedThisTick.TryGetValue(type, out var cur) ? cur + unitsToOrder : unitsToOrder;
        }


        owningNation.constructionSpentThisMonth += spentThisTick;

        // Record (or update) this building as pending in the province's own queue.
        // resourcesCommitted here tracks cumulative units we've placed import
        // orders for (i.e. "set aside" for this project) — not necessarily
        // delivered yet. The everythingArrived check above (against live stock)
        // is what actually gates completion.
        if (pendingEntry == null)
        {
            provinceBuildQueue.Add(new BuildingUnderConstruction
            {
                nation = ownerIndex,
                buildingType = provinceBuilding.buildingType,
                previouslyExisted = !isNewBuilding,
                daysRemaining = building.daysToConstruct,
                province = i,
                resourcesCommittedValue = spentThisTick,
                // NOTE: requires BuildingUnderConstruction to have these two fields:
                //   public Dictionary<ResourceType, float> resourcesRequired;
                //   public Dictionary<ResourceType, float> resourcesCommitted;
                resourcesRequired = new Dictionary<ResourceType, float>(requiredSnapshot),
                resourcesCommitted = orderedThisTick
            });
        }
        else
        {
            pendingEntry.resourcesCommittedValue += spentThisTick;
            pendingEntry.resourcesRequired ??= new Dictionary<ResourceType, float>(requiredSnapshot);
            pendingEntry.resourcesCommitted ??= new Dictionary<ResourceType, float>();

            foreach (var kvp in orderedThisTick)
            {
                pendingEntry.resourcesCommitted[kvp.Key] = pendingEntry.resourcesCommitted.TryGetValue(kvp.Key, out var cur) ? cur + kvp.Value : kvp.Value;
            }
        }
    }

    
    string ExpectedBuildingTypeForResource(string resourceProduced)
    {
        if(resourceProduced == "")
            return null;

        string normalized = resourceProduced.Replace(" ", "");

        if (System.Enum.TryParse<ResourceType>(normalized, true, out var type)
            && extractionBuildingByResource.TryGetValue(type, out var buildingType))
        {
            return buildingType;
        }
        return null;
    }

    string ExpectedConversionBuildingTypeForResource(string resourceProduced)
    {
        string normalized = resourceProduced.Replace(" ", "");
        bool success = System.Enum.TryParse<ResourceType>(normalized, true, out var type);

        if (success && conversionBuildingByResource.TryGetValue(type, out var buildingType))
        {
            return buildingType;
        }
        return null;
    }

    private float GetProvinceResource(ProvinceList province, string resourceName)
    {
        if (resourceName == "Nocks" || resourceName == "Nock")
            return gameData.localCopy.nationList[province.ownerInt].nocks;

        if (System.Enum.TryParse<ResourceType>(resourceName, out var type))
            return province.tradeReciept.GetStock(type).amount;

        return 0f;
    }


    private void AddProvinceResource(ProvinceList province, string resourceName, float amount)
    {
        if (System.Enum.TryParse<ResourceType>(resourceName, out var type))
            province.tradeReciept.GetStock(type).amount += amount;
    }

    private void SubtractProvinceResource(ProvinceList province, string resourceName, float amount)
        => AddProvinceResource(province, resourceName, -amount);



    // How much of `resourceName` this province's conversion buildings collectively
    // want to consume this tick, based on installed active capacity. Used both to
    // generate real import demand (ResourceChange) and to avoid double-booking the
    // same local surplus across multiple converters when estimating new builds.
    float GetConversionInputDemand(ProvinceList province, string resourceName)
    {
        float demand = 0f;
        if (province.buildings == null) return demand;

        foreach (var pb in province.buildings)
        {
            var building = buildings.GetBuildingFromProvinceBuilding(pb.buildingType);
            if (building == null || building.convertsFrom != resourceName) continue;
            demand += pb.activeLevels * producedPerLevel; // mirrors wantsToConsume in RunResourceConversions
        }
        return demand;
    }



}

[System.Serializable]
public class HeldTradeOrder
{
    public int provinceIndex;
    public int goodIndex;
    public float amount;
    public int nationIndex;
    public string orderType; // "Import" or "Export"
}

public enum ConstructionSource { Player, AutoResource, AutoInfrastructure }

public class ConstructionRequest
{
    public int provinceIdx;
    public string buildingType;
    public ConstructionSource source;
    public float priorityScore; // profit estimate for AI sources; player items don't need this
}