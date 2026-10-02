using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;

public class TradeSystem : MonoBehaviour
{
    [Header("Factors")]
    [Tooltip("The percent the Trade Center upcharges to buyers")]
    [SerializeField] private float exportPremium; 
    [SerializeField] private float priceReduction;

    [Header("Script References")]
    [SerializeField] private GameData          gameData;
    [SerializeField] private Buildings         buildings;
    [SerializeField] private SmartTradePath    smartTradePath;
    [SerializeField] private ProvincialEconomy provincialEconomy;
    [SerializeField] private TradeNetworkPathfinder tradeNetworkPathfinder;
    [SerializeField] private MapData           mapData;
    
    public List<NationalTradeLogEntry> playerTradeLog = new List<NationalTradeLogEntry>();
    public Dictionary<int, BestExportClass> closestTradeCenterForProvince; // Keyed by province index
    public Dictionary<int, BestExportClass> closestTradeCenterForImporter;

    

    #region PrimaryTradeCenterLoop
     
    private Dictionary<(int tcIndex, int ownerNation), List<TradeCenterResult>> reachableCenterCache = new();
    private bool tradeNetworkDirty = true; // set true whenever trade centers/ownership change

    List<TradeCenterResult> GetReachableTradeCenters(int tcIndex, int ownerNation)
    {
        if (tradeNetworkDirty)
        {
            reachableCenterCache.Clear();
            tradeNetworkDirty = false;
        }

        var key = (tcIndex, ownerNation);
        if (!reachableCenterCache.TryGetValue(key, out var result))
        {
            result = tradeNetworkPathfinder.FindReachableTradeCenters(tcIndex, ownerNation, 1000000f);
            reachableCenterCache[key] = result;
        }
        return result;
    }

    /* The entry point for the entire system, called by ProvincialEconomy for the neccesary resources to build a building
    or to fulfill upkeep or conversion needs */
    public void PlaceTradeOrder(int provinceIndex, int goodIndex, float amount, int nation, string type)
    {
        TradeGood newTG = new TradeGood
        {
            amount        = amount,
            goodType      = goodIndex,
            provinceIndex = provinceIndex,
            nation        = nation
        };

        if (type == "Export")
            gameData.localCopy.exportOrders.Add(newTG);
        else if (type == "Import")
            gameData.localCopy.importOrders.Add(newTG);
    }

    /* Called by SaveSystem */
    public void Initialize()
    {
        InitializePassageTaxRecords();

        List<int> provincesWithTradeCenters = GetProvincesWithTradeCenters();
        InitializeOceanTaxes();
        tradeNetworkPathfinder.tradeCenterProvinces = provincesWithTradeCenters;
        tradeNetworkPathfinder.RebuildTradeCenterLookup();

        if (!tradeNetworkPathfinder.LoadBaselineNetworkFromFile())
        {
            tradeNetworkPathfinder.PrecomputeBaselineNetwork();
            tradeNetworkPathfinder.SaveBaselineNetworkToFile();
        }

        InitializeTradeCenterPrices(provincesWithTradeCenters);

        for (int i = 0; i < gameData.localCopy.provinceList.Count; i++)
        {
            ProvinceList province = gameData.localCopy.provinceList[i];
            province.tradeClientData = new TradeClientData
            {
                sellToTCForGood = new Dictionary<int, TCDataClass>(),
                buyFromTCForGood = new Dictionary<int, TCDataClass>()
            };
            gameData.localCopy.provinceList[i] = province; 
        }
    }

    public void InitializePassageTaxRecords(){
        gameData.localCopy.passageTaxRecords = new List<PassageTaxRecord>();
        for(int x = 0; x < gameData.localCopy.provinceList.Count; x++){
            PassageTaxRecord newLandPTR = new PassageTaxRecord();
            newLandPTR.isOceanNotLand = false;
            newLandPTR.provinceIdx = x;
            newLandPTR.revenueByNation = new Dictionary<int, float>();
            newLandPTR.totalRevenue = 0;
            gameData.localCopy.passageTaxRecords.Add(newLandPTR);
        }

        for(int y = 0; y < gameData.localCopy.oceanProvinces.Count; y++){
            PassageTaxRecord newLandPTR = new PassageTaxRecord();
            newLandPTR.isOceanNotLand = true;
            newLandPTR.provinceIdx = y;
            newLandPTR.revenueByNation = new Dictionary<int, float>();

            for(int i = 0; i < gameData.localCopy.nationList.Count; i++){
                string constructedKey = $"{i},{y}";
                if(oceanProvinceControl.ContainsKey(constructedKey)){
                    newLandPTR.revenueByNation.Add(i, 0);
                }
            }
            //Everyone with ocean control should get initialized to 0

            newLandPTR.totalRevenue = 0;
            gameData.localCopy.passageTaxRecords.Add(newLandPTR);
        }

        for(int nationIdx = 0; nationIdx < gameData.localCopy.nationList.Count; nationIdx++)
            gameData.localCopy.nationList[nationIdx].lastTradeTaxRevenue = 0;
        

    }


    public void CalculateTradeFlow()
    {
        /*
        * TRADE SYSTEM — OPTION 3: Trade Center Routing
        * ─────────────────────────────────────────────
        * The simplest of the three approaches. Instead of individual probes pathing
        * between every buyer/seller pair, all trade routes through fixed hub buildings.
        *
        * 1. Trade center buildings are initialized by ProvincialEconomy.
        * 2. Every province finds its cheapest-to-reach trade center and sells all of
        *    its goods there (a single lookup + transaction instead of per-good routing).
        * 3. Trade centers reconcile their own surplus/shortfall per good, then find
        *    neighboring trade centers to exchange with to balance supply.
        * 4. Once inter-center exchanges settle, trade centers sell to buyers in their
        *    local markets.
        * 5. Prices adjust based on the resulting supply/demand at each center.
        * 6. Distance costs are applied per hop (province → center, center → center,
        *    center → buyer) rather than per full point-to-point route.
        *
        * The resulting market data (surplus, shortfall, price trends per center) can
        * also feed back into price adjustments and inform AI decisions on where to
        * build new trade-related buildings.
        */

        playerTradeLog.Clear();
        
        InitializePassageTaxRecords();

        SetOceanProvinceControl();

        ReinitializeNations();

        List<int> provincesWithTradeCenters = GetProvincesWithTradeCenters();
        tradeNetworkPathfinder.tradeCenterProvinces = provincesWithTradeCenters;
        InitializeOtherElements(provincesWithTradeCenters);

        for (int i = 0; i < gameData.localCopy.provinceList.Count; i++)
        {
            ProvinceList province = gameData.localCopy.provinceList[i];
            province.tradeClientData = new TradeClientData
            {
                sellToTCForGood = new Dictionary<int, TCDataClass>(),
                buyFromTCForGood = new Dictionary<int, TCDataClass>()
            };
            gameData.localCopy.provinceList[i] = province; 
        }

        var exportOrdersByProvince = BuildOrdersByProvince(gameData.localCopy.exportOrders);
        var importOrdersByProvince = BuildOrdersByProvince(gameData.localCopy.importOrders);

        for (int provinceIndex = 0; provinceIndex < gameData.localCopy.provinceList.Count; provinceIndex++)
        {
            ProvinceList province = gameData.localCopy.provinceList[provinceIndex];
            if (province.ownerInt == -1) continue;

            var exportOrders = exportOrdersByProvince.TryGetValue(provinceIndex, out var provinceExports)
                ? provinceExports
                : new List<TradeGood>();
            var importOrders = importOrdersByProvince.TryGetValue(provinceIndex, out var provinceImports)
                ? provinceImports
                : new List<TradeGood>();

            CalculateBestTradeCenterForProvince(provincesWithTradeCenters, provinceIndex, exportOrders, importOrders);
        }
        
        //Sell the goods to the trade center for each export order
        SellTradeGoodsToTradeCenter();

        //Exchange between trade centers to fill deficits
        ExchangeTradeGoodsBetweenCenters(provincesWithTradeCenters);

        //Sell from the trade center back to the local importers
        SellToImporters(provincesWithTradeCenters);

      //  AssignRevenueBack(provincesWithTradeCenters);

        //Sell back what we didn't sell back the exporters. Refund the distance cost to them

        //Adjust pricing
        //AdjustTradeCenterPricing(provincesWithTradeCenters);

    }

    public void ReinitializeNations()
    {
        for(int idx = 0; idx<gameData.localCopy.nationList.Count; idx++)
        {
            NationList nation = gameData.localCopy.nationList[idx];
            nation.lastTradeRevenue = 0;    
            //nation.roughNockIncome = 0;  
        }
    }


    // Calculate the closest trade center for each province
    public List<int> GetProvincesWithTradeCenters()
    {
        List<int> provincesWithTC = new List<int>();
        for(int i = 0; i < gameData.localCopy.provinceList.Count; i++)
        {
            ProvinceList province = gameData.localCopy.provinceList[i];
            if(province.buildings.Any(b => b.buildingType == "Trade Center"))
                provincesWithTC.Add(i);
        }
        return provincesWithTC;
    }

    /* Initialize the prices in the dictionary for each ProvinceList province in gameData.localCopy.provinceList
    Here we're just starting with 5 for every resource index. We'll also initialize each quantity stored of that resource to 0. 
    It shouldn't start with saved resources */
    public void InitializeTradeCenterPrices(List<int> tradeCenterProvinceIdices)
    {
        closestTradeCenterForImporter = new Dictionary<int, BestExportClass>();
        closestTradeCenterForProvince = new Dictionary<int, BestExportClass>();
        foreach(int tradeCenterIdx in tradeCenterProvinceIdices)
        {
        
            var tradeCenterProvince = gameData.localCopy.provinceList[tradeCenterIdx];
            tradeCenterProvince.tradeCenterData = new TradeCenterData();
            tradeCenterProvince.tradeCenterData.tradeCenterPricing = new Dictionary<int,float>();
            tradeCenterProvince.tradeCenterData.storedTradeCenterGoods = new Dictionary<int,float>();
            tradeCenterProvince.tradeCenterData.inititalStockRecieved = new Dictionary<int,float>();
            tradeCenterProvince.tradeCenterData.stockDemanded = new Dictionary<int,float>();
            tradeCenterProvince.tradeCenterData.revenuePerTradeGood = new Dictionary<int, float>();
            tradeCenterProvince.tradeCenterData.bidsOnOurGoods = new List<Bid>();
            tradeCenterProvince.tradeCenterData.bidsOnOtherTradeCenters = new List<CustomBidTracker>();
            tradeCenterProvince.tradeCenterData.totalTCRevenue = 0;
            
            for(int i = 0; i < buildings.resourceNames.Count+mapData.secondaryTradeGoods.Count; i++)
            {
                tradeCenterProvince.tradeCenterData.tradeCenterPricing.Add(i, 2); 
                tradeCenterProvince.tradeCenterData.revenuePerTradeGood.Add(i, 0);
                tradeCenterProvince.tradeCenterData.storedTradeCenterGoods[i] = 0;
                tradeCenterProvince.tradeCenterData.inititalStockRecieved[i] = 0;
                tradeCenterProvince.tradeCenterData.stockDemanded[i] = 0;
            }
        }
    }

    public void InitializeOtherElements(List<int> tradeCenterProvinceIdices)
    {
        closestTradeCenterForImporter = new Dictionary<int, BestExportClass>();
        closestTradeCenterForProvince = new Dictionary<int, BestExportClass>();
        foreach(int tradeCenterIdx in tradeCenterProvinceIdices)
        {
        
            var tradeCenterProvince = gameData.localCopy.provinceList[tradeCenterIdx];
            tradeCenterProvince.tradeCenterData.storedTradeCenterGoods = new Dictionary<int,float>();
            tradeCenterProvince.tradeCenterData.inititalStockRecieved = new Dictionary<int,float>();
            tradeCenterProvince.tradeCenterData.stockDemanded = new Dictionary<int,float>();
            tradeCenterProvince.tradeCenterData.revenuePerTradeGood = new Dictionary<int, float>();
            tradeCenterProvince.tradeCenterData.bidsOnOurGoods = new List<Bid>();
            tradeCenterProvince.tradeCenterData.bidsOnOtherTradeCenters = new List<CustomBidTracker>();
            tradeCenterProvince.tradeCenterData.totalTCRevenue = 0;
            
            for(int i = 0; i < buildings.resourceNames.Count+mapData.secondaryTradeGoods.Count; i++)
            {
                tradeCenterProvince.tradeCenterData.revenuePerTradeGood.Add(i, 0);
                tradeCenterProvince.tradeCenterData.storedTradeCenterGoods[i] = 0;
                tradeCenterProvince.tradeCenterData.inititalStockRecieved[i] = 0;
                tradeCenterProvince.tradeCenterData.stockDemanded[i] = 0;
            }
        }        
    }

    Dictionary<int, List<TradeGood>> BuildOrdersByProvince(List<TradeGood> orders)
    {
        Dictionary<int, List<TradeGood>> ordersByProvince = new Dictionary<int, List<TradeGood>>();
        foreach (TradeGood order in orders)
        {
            if (!ordersByProvince.TryGetValue(order.provinceIndex, out var provinceOrders))
            {
                provinceOrders = new List<TradeGood>();
                ordersByProvince[order.provinceIndex] = provinceOrders;
            }

            provinceOrders.Add(order);
        }

        return ordersByProvince;
    }

    /* Pick one overall trade center for a province by combining the costs and profits across
    that province's current export and import orders. This keeps a province on a single hub
    instead of letting different goods route to different centers. */
    void CalculateBestTradeCenterForProvince(List<int> provinceIdicesWithTradeCenters, int provinceIndex, List<TradeGood> exportOrders, List<TradeGood> importOrders)
    {
        ProvinceList province = gameData.localCopy.provinceList[provinceIndex];
        if (province.ownerInt == -1) return;

        float bestScore = -999999999f;
        int bestTradeCenter = -1;
        List<SegmentClass> bestPath = new List<SegmentClass>();
        float bestDistanceCost = 0f;

        foreach (int provinceIdxwithTC in provinceIdicesWithTradeCenters)
        {
            var tradeCenterProvince = gameData.localCopy.provinceList[provinceIdxwithTC];
            NationList ownerNation = gameData.localCopy.nationList[province.ownerInt];
            if (!ownerNation.provincesDiscovered.Contains(provinceIdxwithTC)) continue;
            if (Vector2.Distance(province.position, tradeCenterProvince.position) > 400f) continue;

            var path = smartTradePath.FetchPathForNation(provinceIndex, provinceIdxwithTC, province.ownerInt);
            if (path == null || path.Count == 0) continue;

            float distTTC = path[path.Count - 1].distanceCost;
            float score = 0f;

            foreach (TradeGood exportOrder in exportOrders)
            {
                if (!tradeCenterProvince.tradeCenterData.tradeCenterPricing.ContainsKey(exportOrder.goodType)) continue;
                float price = tradeCenterProvince.tradeCenterData.tradeCenterPricing[exportOrder.goodType];
                score += (price - distTTC) * exportOrder.amount;
            }

            foreach (TradeGood importOrder in importOrders)
            {
                if (!tradeCenterProvince.tradeCenterData.tradeCenterPricing.ContainsKey(importOrder.goodType)) continue;
                float price = tradeCenterProvince.tradeCenterData.tradeCenterPricing[importOrder.goodType];
                score -= (price + distTTC) * importOrder.amount;
            }

            if (exportOrders.Count == 0 && importOrders.Count == 0)
                score -= distTTC;

            if (score > bestScore)
            {
                bestScore = score;
                bestTradeCenter = provinceIdxwithTC;
                bestPath = path;
                bestDistanceCost = distTTC;
            }
        }

        if (bestTradeCenter == -1) return;

        BestExportClass bestExport = new BestExportClass
        {
            destinationTradeCenter = bestTradeCenter,
            path = bestPath
        };

        closestTradeCenterForProvince[provinceIndex] = bestExport;
        closestTradeCenterForImporter[provinceIndex] = bestExport;

        var relevantGoods = new HashSet<int>();
        foreach (TradeGood exportOrder in exportOrders)
            relevantGoods.Add(exportOrder.goodType);
        foreach (TradeGood importOrder in importOrders)
            relevantGoods.Add(importOrder.goodType);

        foreach (int goodType in relevantGoods)
        {
            province.tradeClientData.sellToTCForGood[goodType] = new TCDataClass
            {
                destinationTCIdx = bestTradeCenter,
                distanceCost = bestDistanceCost
            }; 

            province.tradeClientData.buyFromTCForGood[goodType] = new TCDataClass
            {
                destinationTCIdx = bestTradeCenter,
                distanceCost = bestDistanceCost
            };
        }
    }
    
    /*
    This method handles the initial sale from linked exporters to their best trade center. For each export order they'll travel to 
    the trade center in this one tick and pay out passage fees to all the nations charging along the way. 
    Then the trade center will pay out the cost of the distance, trade taxes, and the price, with the seller getting what reamins of the price.
    Then the resources are exchanged. Now the trade center has resources
    */
    void SellTradeGoodsToTradeCenter()
    {
        //Along the way, pay out the passage fees
        for(int i = 0; i < gameData.localCopy.exportOrders.Count; i++)
        {
            TradeGood exportOrder = gameData.localCopy.exportOrders[i];

            int exportProvinceIdx = exportOrder.provinceIndex;
            ProvinceList exportProvince = gameData.localCopy.provinceList[exportProvinceIdx];

            if(!closestTradeCenterForProvince.TryGetValue(exportProvinceIdx, out var exportClass)) continue;
            ProvinceList tradeCenterProvince = gameData.localCopy.provinceList[exportClass.destinationTradeCenter];

            //Pay out the passage fees
            float totalPassageFee = 0;
            float totalDistanceCost = 0;
            foreach(SegmentClass segment in exportClass.path)
            {
                float passageFee = PayPassageFee(segment);
                totalPassageFee += passageFee;
            }
            totalDistanceCost = exportClass.path[exportClass.path.Count-1].distanceCost;

            NationList exportingNation = gameData.localCopy.nationList[exportProvince.ownerInt];

            float priceForGood = tradeCenterProvince.tradeCenterData.tradeCenterPricing[exportOrder.goodType];
            float netRevenue = (priceForGood-totalPassageFee-totalDistanceCost)*exportOrder.amount* priceReduction;
            exportingNation.lastTradeRevenue += netRevenue;

            // Trade receipt logic: this is the moment the exporting province's own
            // production actually gets sold, so it's the correct place to feed
            // ResourceEntry.sold / .revenue — these are what shareOfProfitforProvince
            // (in ProvincialEconomy) divides to get a per-unit price for that province's
            // extraction buildings. Previously nothing ever wrote to `sold`, which made
            // that ratio permanently 0 for anything routed through a trade center.
            ResourceEntry exportEntry = exportProvince.tradeReciept.Get((ResourceType)exportOrder.goodType);
            exportEntry.sold += exportOrder.amount;
            exportEntry.revenue += netRevenue;

            exportProvince.exportLog ??= new List<ExportLog>();
            ExportLog newEL = new ExportLog();
            newEL.resourceSold = buildings.unifiedResourceNames[exportOrder.goodType];
            newEL.provinceSoldToIdx = exportClass.destinationTradeCenter;
            newEL.nationSoldToIdx = tradeCenterProvince.ownerInt;
            newEL.amount = exportOrder.amount;
            newEL.revenueEarned = netRevenue;
            newEL.distanceCost = totalDistanceCost*exportOrder.amount;
            newEL.path = exportClass.path;
            newEL.pathOriginIdx = exportProvinceIdx; 

            exportProvince.exportLog.Add(newEL);

            tradeCenterProvince.tradeCenterData.totalTCRevenue -= priceForGood*exportOrder.amount* priceReduction;
            tradeCenterProvince.tradeCenterData.revenuePerTradeGood[exportOrder.goodType] -= priceForGood*exportOrder.amount* priceReduction;
            tradeCenterProvince.tradeCenterData.storedTradeCenterGoods[exportOrder.goodType] += exportOrder.amount;
            tradeCenterProvince.tradeCenterData.inititalStockRecieved[exportOrder.goodType] += exportOrder.amount;

            tradeCenterProvince.importLog ??= new List<ImportLog>();
            ImportLog newTIL = new ImportLog();
            newTIL.resourceBought = buildings.unifiedResourceNames[exportOrder.goodType];
            newTIL.provinceBoughtFromIdx = exportProvinceIdx;
            newTIL.nationBoughtFromIdx = exportProvince.ownerInt;
            newTIL.amount = exportOrder.amount;
            newTIL.amountSpent = netRevenue;
            newTIL.distanceCost = totalDistanceCost*exportOrder.amount;
            newTIL.path = exportClass.path;
            newTIL.pathOriginIdx = exportProvinceIdx;

            tradeCenterProvince.importLog.Add(newTIL);

            //Logging for the player
            if (exportProvince.ownerInt == mapData.playerNation){
                playerTradeLog.Add(new NationalTradeLogEntry
                {
                    tradeType = TradeLogType.ExportTrade,
                    goodType = exportOrder.goodType,
                    goodName = buildings.unifiedResourceNames[exportOrder.goodType],
                    amount = exportOrder.amount,
                    provinceFrom = exportProvinceIdx,
                    provinceTo = exportClass.destinationTradeCenter,
                    distanceCost = totalDistanceCost * exportOrder.amount,
                    price = priceForGood,
                    totalCost = netRevenue
                });
            }


            SubtractResourceFromProvince(exportProvinceIdx, exportOrder.goodType, exportOrder.amount);
        }
    }

    /* Takes in a province index, an integer for a good type, and an amount to subtract.
        Each int maps to a ResourceType, so we route straight through TradeReciept's
        stock list rather than the old per-field switch (province.iron, province.copper, etc.
        no longer exist post-refactor). Note: `stock` lives on a class (TradeReciept),
        so this mutates in place even if ProvinceList itself is a struct — no write-back needed. */
    void SubtractResourceFromProvince(int provinceIndex, int goodType, float amount)
    {
        ProvinceList province = gameData.localCopy.provinceList[provinceIndex];
        ResourceType type = (ResourceType)goodType;
        province.tradeReciept.GetStock(type).amount -= amount;
    }

    /* Takes in a segmentclass from SmartTradePath, and for land provinces it will pay out the passage tax to the owner.
        Ocean provinces are not yet handled. If the province is not owned, no passage fee is paid.
    */
    float PayPassageFee(SegmentClass segment)
    {
      //  Debug.Log(segment.passageFee);
        if (segment.passageFee <= 0) return 0;

        if (!segment.oceanNotLand)
        {
            ProvinceList segmentProvince = gameData.localCopy.provinceList[segment.landIndex];
            if (segmentProvince.ownerInt == -1) return 0;

            NationList segmentOwner = gameData.localCopy.nationList[segmentProvince.ownerInt];
            segmentOwner.lastTradeTaxRevenue += segment.passageFee;
            return segment.passageFee;
        }

        return PayOceanPassageFee(segment.oceanIndex, segment.passageFee);
    }

    /// <summary>
    /// Ocean passage revenue is split among every nation with a tax stake in
    /// this ocean province, weighted the same way RecalculateTradeTaxOfOceanProvince
    /// blends their rates into the single universal passageTax value — a nation
    /// with 70% control of the tax rate also collects 70% of what gets paid here.
    /// </summary>
    float PayOceanPassageFee(int oceanProvinceIdx, float fee)
    {
        var taxes = gameData.localCopy.oceanTradeTaxes;
        if (taxes == null || oceanProvinceIdx < 0 || oceanProvinceIdx >= taxes.Count) return 0;

        OceanTradeTax oTT = taxes[oceanProvinceIdx];
        if (oTT?.nationOceanTradeTaxes == null || oTT.nationOceanTradeTaxes.Count == 0) return 0;

        // Weight by (nationTax * nationControl) — each nation's actual contribution
        // to the blended rate in RecalculateTradeTaxOfOceanProvince — not by control
        // alone. A high-control nation charging 0 contributed nothing to the rate
        // and should collect nothing from it.
        float totalWeightedTax = 0f;
        foreach (var n in oTT.nationOceanTradeTaxes)
            totalWeightedTax += n.nationTax * n.nationControl;

        if (totalWeightedTax <= 0f) return 0;

        float total = 0;
        PassageTaxRecord pTR = gameData.localCopy.passageTaxRecords.FirstOrDefault(p => p.isOceanNotLand == true && p.provinceIdx == oceanProvinceIdx);

        foreach (var nationalOTT in oTT.nationOceanTradeTaxes)
        {
            if (nationalOTT.nationIndex < 0 || nationalOTT.nationIndex >= gameData.localCopy.nationList.Count) continue;

            float contribution = nationalOTT.nationTax * nationalOTT.nationControl;
            if (contribution <= 0f) continue;

            float share = fee * (contribution / totalWeightedTax);
            gameData.localCopy.nationList[nationalOTT.nationIndex].lastTradeTaxRevenue += share;

            //Record the per-nation revenue for analyzation purposes
            if(!pTR.revenueByNation.ContainsKey(nationalOTT.nationIndex))
                pTR.revenueByNation.Add(nationalOTT.nationIndex, share);
            else 
                pTR.revenueByNation[nationalOTT.nationIndex] = share; 
            total += share;
        }

        pTR.totalRevenue = total;



        return fee;
    }

    /* Master loop of center exchange, first place the bids on the other trade centers, then go through and accept them */
    public void ExchangeTradeGoodsBetweenCenters(List<int> tradeCenterIdices)
    {
        PlaceBids(tradeCenterIdices);
        AcceptBids(tradeCenterIdices);
    }
    
    /* 
        Here we go through each trade center, we find what we are lacking, and look for provinces that have too much of that
        (we do this by looking at the current stock which we just bought, and the current demand in import orders on our province)
        Then we look at the trade centers in range and the distance cost to them, then place our bid as our province price for it minus the distance cost.
        The distance cost naturally includes the passage taxes
    */
    void PlaceBids(List<int> tradeCenterIdices)
    {
        // Compute once per trade center per tick — shared by every comparison below
        var deficitCache = new Dictionary<int, Dictionary<int, float>>();
        Dictionary<int, float> GetOrComputeDeficits(int tcIdx)
        {
            if (!deficitCache.TryGetValue(tcIdx, out var d))
            {
                var province = gameData.localCopy.provinceList[tcIdx];
                (_, d) = CalculateExcessesAndDeficitsForTC(province, tcIdx);
                deficitCache[tcIdx] = d;
            }
            return d;
        }

        foreach(int tradeCenterIndex in tradeCenterIdices)
        {
            ProvinceList tradeCenterProvince = gameData.localCopy.provinceList[tradeCenterIndex];
            var excessesAndDeficits = GetOrComputeDeficits(tradeCenterIndex);

            bool hasAny = excessesAndDeficits.Values.Any(v => v != 0);
            if (!hasAny || tradeCenterProvince.ownerInt == -1) continue;

            List<TradeCenterResult> possibleTradeCenters =
                tradeNetworkPathfinder.FindReachableTradeCenters(tradeCenterIndex, tradeCenterProvince.ownerInt, 1000000f);
            if (possibleTradeCenters == null || possibleTradeCenters.Count == 0) continue;

            // Loop order flipped: compute the other center's deficits ONCE, not per-good
            foreach(TradeCenterResult tradeCenterResult in possibleTradeCenters)
            {
                ProvinceList otherTradeCenterProvince = gameData.localCopy.provinceList[tradeCenterResult.tradeCenterProvince];
                var otherDeficits = GetOrComputeDeficits(tradeCenterResult.tradeCenterProvince); // fixed: correct tcIndex, cached

                for(int i = 0; i < buildings.resourceNames.Count+mapData.secondaryTradeGoods.Count; i++)
                {
                    if (excessesAndDeficits[i] >= 0) continue;      // no deficit for us
                    if (otherDeficits[i] <= 0) continue;            // they have no excess

                    float transitCost = tradeCenterResult.cumulativeTradeCost;

                    Bid newBid = new Bid
                    {
                        price = tradeCenterProvince.tradeCenterData.tradeCenterPricing[i] - transitCost,
                        distancePrice = transitCost,
                        path = CombineRoute(tradeCenterResult.route),
                        amount = Mathf.Min(Mathf.Abs(excessesAndDeficits[i]), Mathf.Abs(otherDeficits[i])),
                        senderIndex = tradeCenterIndex,
                        tradeGoodType = i
                    };
                    otherTradeCenterProvince.tradeCenterData.bidsOnOurGoods.Add(newBid);

                    tradeCenterProvince.tradeCenterData.bidsOnOtherTradeCenters.Add(new CustomBidTracker
                    {
                        tradeGoodType = i,
                        tradeCenterFromIdx = tradeCenterIndex,
                        tradeCenterPlacedOnIdx = tradeCenterResult.tradeCenterProvince
                    });
                }
            }
        }
    }

    /* Go through all the bids on all the trade goods on each trade center and accept the best offer until offers run out or our excess does.
        Then make the transfer */
    void AcceptBids(List<int> tradeCenterIdices)
    {
        foreach(int tradeIdx in tradeCenterIdices)
        {
            ProvinceList tradeCenterProvince = gameData.localCopy.provinceList[tradeIdx];
            (bool canDo, Dictionary<int, float> excessDictionary) = CalculateExcessesAndDeficitsForTC(tradeCenterProvince, tradeIdx);

            for(int i = 0; i < buildings.resourceNames.Count+mapData.secondaryTradeGoods.Count; i++)
            {
                float excess = excessDictionary[i];
                bool succeedingExcess = ExcessSucceeds(excess, tradeCenterProvince, i);

                while (succeedingExcess)
                {
                    //Find the best bid
                    var bidsOnResource = tradeCenterProvince.tradeCenterData.bidsOnOurGoods.Where(b => b.tradeGoodType == i && b.amount > 0);
                    Bid bestBid = null;
                    float bestBidPrice = -Mathf.Infinity;
                    foreach(Bid bid in bidsOnResource)
                    {
                        if(bid.price > bestBidPrice)
                        {
                            bestBid = bid;
                            bestBidPrice = bid.price;
                        }
                    }


                    if(bestBid != null && bestBidPrice > 0)
                    {
                        ProvinceList sellingToProvince = gameData.localCopy.provinceList[bestBid.senderIndex];

                        float amount = Mathf.Min(bestBid.amount, excess);
                        tradeCenterProvince.tradeCenterData.storedTradeCenterGoods[i] -= amount;
                        sellingToProvince.tradeCenterData.storedTradeCenterGoods[i] += amount;
                        tradeCenterProvince.tradeCenterData.stockDemanded[i] += amount;

                        //Actually decrement the working excess so the loop can terminate
                        excess -= amount;

                        //Reduce the bid itself
                        float amountRemaining = bestBid.amount - amount;
                        bestBid.amount = amountRemaining;

                        //Reduce/remove the mirrored bid tracked on the other side
                        if(sellingToProvince.tradeCenterData.bidsOnOtherTradeCenters != null)
                        {
                            List<CustomBidTracker> bidsOnOtherProvinces = sellingToProvince.tradeCenterData.bidsOnOtherTradeCenters
                                .Where(b => b.tradeGoodType == i && b.tradeCenterFromIdx == bestBid.senderIndex && b.tradeCenterPlacedOnIdx == tradeIdx)
                                .ToList();

                            if(amountRemaining <= 0f)
                            {
                                for(int k = bidsOnOtherProvinces.Count-1; k >= 0; k--)
                                    sellingToProvince.tradeCenterData.bidsOnOtherTradeCenters.Remove(bidsOnOtherProvinces[k]);
                            }

                            foreach(CustomBidTracker customBid in bidsOnOtherProvinces)
                            {
                                var otherBidOnProvince = gameData.localCopy.provinceList[customBid.tradeCenterPlacedOnIdx];
                                Bid relatedBid = otherBidOnProvince.tradeCenterData.bidsOnOurGoods.FirstOrDefault(b => b.senderIndex == customBid.tradeCenterFromIdx && b.tradeGoodType == customBid.tradeGoodType);
                                if(relatedBid != null)
                                    relatedBid.amount = amountRemaining;
                            }
                        }

                        if(bestBid.amount <= 0f)
                            tradeCenterProvince.tradeCenterData.bidsOnOurGoods.Remove(bestBid);

                        //Now exchange the money and pay the passage taxes
                        float totalPassageFee = 0;
                        foreach(SegmentClass segment in bestBid.path)
                            totalPassageFee += PayPassageFee(segment);

                        float totalDistanceCost = bestBid.path[bestBid.path.Count-1].distanceCost;
                        float wealthExchanged = amount*bestBidPrice;

                        tradeCenterProvince.tradeCenterData.totalTCRevenue += wealthExchanged;
                        tradeCenterProvince.tradeCenterData.revenuePerTradeGood[i] += wealthExchanged - ((totalPassageFee-totalDistanceCost) * amount);
                        sellingToProvince.tradeCenterData.totalTCRevenue -= wealthExchanged;
                        sellingToProvince.tradeCenterData.revenuePerTradeGood[i] -= wealthExchanged;

                        tradeCenterProvince.exportLog ??= new List<ExportLog>();
                        tradeCenterProvince.exportLog.Add(new ExportLog
                        {
                            resourceSold      = buildings.unifiedResourceNames[i],
                            provinceSoldToIdx = bestBid.senderIndex,
                            nationSoldToIdx   = sellingToProvince.ownerInt,
                            amount            = amount,
                            revenueEarned     = wealthExchanged,
                            distanceCost      = totalDistanceCost * amount,
                            path              = bestBid.path,
                            pathOriginIdx     = bestBid.senderIndex
                        });

                        sellingToProvince.importLog ??= new List<ImportLog>();
                        sellingToProvince.importLog.Add(new ImportLog
                        {
                            resourceBought        = buildings.unifiedResourceNames[i],
                            provinceBoughtFromIdx = tradeIdx,
                            nationBoughtFromIdx   = tradeCenterProvince.ownerInt,
                            amount                = amount,
                            amountSpent           = wealthExchanged,
                            distanceCost          = totalDistanceCost * amount,
                            path                  = bestBid.path,
                            pathOriginIdx         = bestBid.senderIndex
                        });

                        if (sellingToProvince.ownerInt == mapData.playerNation)
                        {
                            playerTradeLog.Add(new NationalTradeLogEntry
                            {
                                tradeType = TradeLogType.ExportTrade, // Represents an export from one TC to another
                                goodType = i,
                                goodName = buildings.unifiedResourceNames[i],
                                amount = amount,
                                provinceFrom = tradeIdx,
                                provinceTo = bestBid.senderIndex,
                                distanceCost = totalDistanceCost * amount,
                                price = bestBidPrice,
                                totalCost = wealthExchanged
                            });
                        }
                    }
                    else
                        break;

                    //Renew the condition using the locally-tracked excess, not the stale dictionary
                    succeedingExcess = ExcessSucceeds(excess, tradeCenterProvince, i);
                }
            }
        }
    }

    /*Little helper for when to exit the while loop. False if no excess, bids, or bids of the correct trade good*/
    bool ExcessSucceeds(float excess, ProvinceList tradeCenterProvince, int i)
    {
        if (excess <= 0) return false;
        else if (tradeCenterProvince.tradeCenterData.bidsOnOurGoods == null) return false;
        else if (tradeCenterProvince.tradeCenterData.bidsOnOurGoods.Count == 0) return false;
        else if(!tradeCenterProvince.tradeCenterData.bidsOnOurGoods.Any(b => b.tradeGoodType == i)) return false;
        
        return true;
    }

    //Helper to combine the lists of segmentclasses on tradeleg rout into one list of segmentclass
    List<SegmentClass> CombineRoute(List<TradeLeg> route)
    {
        List<SegmentClass> result = new List<SegmentClass>();
        foreach(TradeLeg tradeLeg in route)
        {
            foreach(SegmentClass segment in tradeLeg.route)
            {
                result.Add(segment);
            }    
        }

        return result;
    }

    /* Help method to find if there are any excess or deficits for a trade center when calculating the stock bought versus the orders
    for us on those goods, also, for the index of the trading good return a float of the amount. Greater than 0 = excess, less than is a deficit to be filled */
    (bool, Dictionary<int, float>) CalculateExcessesAndDeficitsForTC(ProvinceList tradeCenterProvince, int tcIndex)
    {
        Dictionary<int, float> results = new Dictionary<int, float>();
        bool hasExcessesOrDeficits = false;
        for(int i = 0; i < buildings.resourceNames.Count+mapData.secondaryTradeGoods.Count; i++)
        {
            if(tradeCenterProvince.tradeCenterData.storedTradeCenterGoods.ContainsKey(i))
                results[i] = tradeCenterProvince.tradeCenterData.storedTradeCenterGoods[i];
            else    
                results[i] = 0;
        }

        //Iterate through the import orders for the demand
        foreach(TradeGood importOrder in gameData.localCopy.importOrders)
        {
            if(!closestTradeCenterForImporter.TryGetValue(importOrder.provinceIndex, out var importClass)) continue;
            if(importClass.destinationTradeCenter != tcIndex) continue;

            results[importOrder.goodType] -= importOrder.amount;
        }

        for(int i = 0; i < buildings.resourceNames.Count+mapData.secondaryTradeGoods.Count; i++)
        {
            if(results[i] != 0)
                hasExcessesOrDeficits = true;
        }
        

        return (hasExcessesOrDeficits, results);
    }


    /* Go through all of the provinces with a trade center, and for every resource they have in stock, find all the people wanting 
    to import it. Distribute it around with a changing average so that every gets the minimum of either how much they need or how much is available,
    then the exchange is made */
    void SellToImporters(List<int> provinceWithTCIdices)
    {
        foreach(int provinceWithTCIdx in provinceWithTCIdices)
        {
            ProvinceList TCProvince = gameData.localCopy.provinceList[provinceWithTCIdx];
            for(int i = 0; i < buildings.resourceNames.Count+mapData.secondaryTradeGoods.Count; i++)
            {
                float amountToGoAround = TCProvince.tradeCenterData.storedTradeCenterGoods[i];
                List<TradeGood> attemptedImportersHere = importersForTradeCenterAndResource(provinceWithTCIdx, i);
                foreach(TradeGood importer in attemptedImportersHere)
                {
                    TCProvince.tradeCenterData.stockDemanded[i] += importer.amount;
                }

                //min average
                if(amountToGoAround == 0) continue;    
                
                foreach(TradeGood attemptedImporter in attemptedImportersHere)
                {
                    float averageAmount = amountToGoAround/attemptedImportersHere.Count;
                    
                    float amount = Mathf.Min(averageAmount, attemptedImporter.amount);
                    amountToGoAround -= averageAmount;

                    //Pay passage cost
                    ProvinceList importProvince = gameData.localCopy.provinceList[attemptedImporter.provinceIndex];
                    if(!closestTradeCenterForImporter.TryGetValue(attemptedImporter.provinceIndex, out var importClass)) continue;
                    
                    float totalPassageFee = 0;
                    foreach(SegmentClass segment in importClass.path)
                        totalPassageFee += PayPassageFee(segment);

                    float totalDistanceCost = importClass.path[importClass.path.Count-1].distanceCost;

                    NationList importingNation = gameData.localCopy.nationList[importProvince.ownerInt];

                    float priceForGood = TCProvince.tradeCenterData.tradeCenterPricing[attemptedImporter.goodType];
                    float cost = (priceForGood-totalPassageFee-totalDistanceCost)*amount*exportPremium;
                    importingNation.lastTradeExpense += cost * priceReduction;

                    // Trade receipt logic: this is the actual purchase event for the
                    // importing province. Previously ProvinceReciept.expense was only ever
                    // touched by ProvincialEconomy for LOCALLY used resources — real
                    // imports bought through a trade center never showed up on the
                    // province's own receipt, so resourceExpenseForResource's
                    // (expense / amountReceived) ratio was blind to actual import prices.
                    ResourceEntry importEntry = importProvince.tradeReciept.Get((ResourceType)attemptedImporter.goodType);
                    importEntry.expense += cost * priceReduction;
                    
                    importProvince.importLog ??= new List<ImportLog>();
                    ImportLog newIL = new ImportLog();
                    newIL.resourceBought = buildings.unifiedResourceNames[attemptedImporter.goodType];
                    newIL.provinceBoughtFromIdx = provinceWithTCIdx;
                    newIL.nationBoughtFromIdx = TCProvince.ownerInt;
                    newIL.amount = amount;
                    newIL.amountSpent = cost * priceReduction;
                    newIL.distanceCost = totalDistanceCost*amount;
                    newIL.path = importClass.path;
                    importProvince.importLog.Add(newIL);
                    newIL.pathOriginIdx = attemptedImporter.provinceIndex;  

                    TCProvince.tradeCenterData.totalTCRevenue += priceForGood*attemptedImporter.amount*exportPremium* priceReduction;
                    TCProvince.tradeCenterData.revenuePerTradeGood[attemptedImporter.goodType] += priceForGood*exportPremium*attemptedImporter.amount * priceReduction;
                    TCProvince.tradeCenterData.storedTradeCenterGoods[attemptedImporter.goodType] -= amount;
                                        
                                        
                    TCProvince.importLog ??= new List<ImportLog>();
                    ExportLog newTEL = new ExportLog();
                    newTEL.resourceSold = buildings.unifiedResourceNames[attemptedImporter.goodType];
                    newTEL.provinceSoldToIdx = attemptedImporter.provinceIndex;
                    newTEL.nationSoldToIdx = importProvince.ownerInt;
                    newTEL.amount = amount;
                    newTEL.revenueEarned = cost * priceReduction;
                    newTEL.distanceCost = totalDistanceCost*amount;
                    newTEL.path = importClass.path;
                    newTEL.pathOriginIdx = attemptedImporter.provinceIndex;

                    TCProvince.exportLog.Add(newTEL);

                    if (importProvince.ownerInt == mapData.playerNation){
                        playerTradeLog.Add(new NationalTradeLogEntry
                        {
                            tradeType = TradeLogType.ImportTrade,
                            goodType = attemptedImporter.goodType,
                            goodName = buildings.unifiedResourceNames[attemptedImporter.goodType],
                            amount = amount,
                            provinceFrom = provinceWithTCIdx,
                            provinceTo = attemptedImporter.provinceIndex,
                            distanceCost = totalDistanceCost * amount,
                            price = priceForGood,
                            totalCost = cost * priceReduction
                        });
                    }

                    //We'll actually add it, so negative amount
                    SubtractResourceFromProvince(attemptedImporter.provinceIndex, attemptedImporter.goodType, -amount);
                }
            }
        }
    }



    /* Find all of the import trade orders for a certain trade center index and a trade good index */
    List<TradeGood> importersForTradeCenterAndResource(int tradeCenterIdx, int tradeGood)
    {
        List<TradeGood> result = new List<TradeGood>();
        List<TradeGood> importersForGood = gameData.localCopy.importOrders.Where(i => i.goodType == tradeGood).ToList();
        foreach(TradeGood importer in importersForGood)
        {
            if(!closestTradeCenterForImporter.TryGetValue(importer.provinceIndex, out var bestImporter)) continue;
            if(bestImporter.destinationTradeCenter == tradeCenterIdx)
                result.Add(importer);
        }

        return result;
    }

    void AssignRevenueBack(List<int> provincesWithTradeCenters)
    {
        /*
        foreach(int tradeCenterIdx in provincesWithTradeCenters)
        {
            var tradeCenterProvince = gameData.localCopy.provinceList[tradeCenterIdx];
            if(tradeCenterProvince.ownerInt == -1) continue;
            var tradeCenterNation = gameData.localCopy.nationList[tradeCenterProvince.ownerInt];

            if(tradeCenterProvince.tradeCenterData.totalTCRevenue > 0)
            {
                //Profit!
                tradeCenterNation.lastTradeRevenue += Mathf.Abs(tradeCenterProvince.tradeCenterData.totalTCRevenue) * priceReduction;

            }
            else
            {
                //Loss
                tradeCenterNation.lastTradeExpense += Mathf.Abs(tradeCenterProvince.tradeCenterData.totalTCRevenue) * priceReduction;

            }
        }*/
    }


    void AdjustTradeCenterPricing(List<int> provincesWithTradeCenters)
    {
        const float SELL_THROUGH_HIGH = 0.95f;
        const float SELL_THROUGH_LOW = 0.65f;
        const float PRICE_UP = 1.05f;
        const float PRICE_DOWN = 0.95f;
        const float MIN_PRICE = 0.1f;
        const float MAX_PRICE = 10f;

        foreach (int tradeCenterIdx in provincesWithTradeCenters)
        {
            var tradeCenterProvince = gameData.localCopy.provinceList[tradeCenterIdx];
            var tcData = tradeCenterProvince.tradeCenterData;

            for (int resourceIndex = 0; resourceIndex < buildings.resourceNames.Count+mapData.secondaryTradeGoods.Count; resourceIndex++)
            {
                float initialAmount = tcData.inititalStockRecieved[resourceIndex];
                if (initialAmount <= 0f)
                    continue; // nothing was stocked, nothing to evaluate

                float remainingAmount = tcData.storedTradeCenterGoods[resourceIndex]; //100 remaining and 200 initial = 0.5
                float importerDemand = tcData.stockDemanded[resourceIndex];
                float sellThroughRatio = remainingAmount / initialAmount;

                //Sell a lot = we can raise prices
                //Sell too little = lower prices
                if (sellThroughRatio <= SELL_THROUGH_LOW)
                {
                    tcData.tradeCenterPricing[resourceIndex] *= PRICE_UP;
                }
                else if (sellThroughRatio > SELL_THROUGH_HIGH)
                {
                    tcData.tradeCenterPricing[resourceIndex] *= PRICE_DOWN;
                }

                //Not buy enough to cover sales = raise buy price
                //Buy too much = lower buy price

                // Clamp so prices don't run away in either direction
                tcData.tradeCenterPricing[resourceIndex] = Mathf.Clamp(
                    tcData.tradeCenterPricing[resourceIndex], MIN_PRICE, MAX_PRICE);
            }
        }
    }


    #endregion PrimaryTradeSystemLoop


    /* Now for outside helpers */

    // AI CODE BEGIN
    /* Quick estimate lookup: given a province and a resource, returns which trade center
    they'd buy from and what it would currently cost them (trade center's spot price + 
    distance cost to get there). Doesn't require an existing import order for the good —
    unlike GetResourcePriceForProvinceandTradeGood, which only resolves for goods already
    in tradeClientData.buyFromTCForGood. Returns null if the province has no resolved
    trade center this tick, or the good isn't tracked at that center. */
    public ProvinceTradeEstimate? EstimateResourceCostForProvince(int provinceIndex, int goodType)
    {
        if (!closestTradeCenterForImporter.TryGetValue(provinceIndex, out var bestExport))
            return null;

        ProvinceList tradeCenterProvince = gameData.localCopy.provinceList[bestExport.destinationTradeCenter];
        if (!tradeCenterProvince.tradeCenterData.tradeCenterPricing.TryGetValue(goodType, out float basePrice))
            return null;

        float distanceCost = bestExport.path.Count > 0
            ? bestExport.path[bestExport.path.Count - 1].distanceCost
            : 0f;

        return new ProvinceTradeEstimate
        {
            tradeCenterProvinceIdx = bestExport.destinationTradeCenter,
            basePrice              = basePrice,
            distanceCost           = distanceCost,
            estimatedCost          = basePrice + distanceCost
        };
    }
    //AI CODE END

    public float GetResourcePriceForProvinceandTradeGood(int provinceIdx, int tradeGoodIdx)
    {
        ProvinceList province = gameData.localCopy.provinceList[provinceIdx];

        if (!province.tradeClientData.buyFromTCForGood.TryGetValue(tradeGoodIdx, out var tcData))
            return 0f;

        ProvinceList tradeCenter = gameData.localCopy.provinceList[tcData.destinationTCIdx];
        return tradeCenter.tradeCenterData.tradeCenterPricing[tradeGoodIdx] + tcData.distanceCost;
    }

    void InitializeOceanTaxes()
    {
        gameData.localCopy.oceanTradeTaxes = new List<OceanTradeTax>();
        for(int i = 0; i < gameData.localCopy.oceanProvinces.Count; i++)
        {
            OceanTradeTax newOTT = new OceanTradeTax();
            newOTT.nationOceanTradeTaxes = new List<NationalOTT>();
            gameData.localCopy.oceanTradeTaxes.Add(newOTT);
        }
    }

    public void SetTradeTaxOfOceanProvince(int oceanProvinceIdx) 
        => gameData.localCopy.oceanProvinces[oceanProvinceIdx].passageTax = RecalculateTradeTaxOfOceanProvince(oceanProvinceIdx);

    float RecalculateTradeTaxOfOceanProvince(int oceanProvinceIdx)
    {
        OceanTradeTax oTT = gameData.localCopy.oceanTradeTaxes[oceanProvinceIdx];
        if (oTT.nationOceanTradeTaxes == null) return 0;

        float totalValue = 0;
        float totalControl = 0;

        //40% tax 20% control
        //30% tax 70% control
        //80% tax 10% control

        //0.08+0.021 + 0.08 = 0.181 tax

        foreach(NationalOTT nationalOTT in oTT.nationOceanTradeTaxes)
        {
            totalValue += nationalOTT.nationTax * nationalOTT.nationControl; 
            totalControl += nationalOTT.nationControl;
        }

        float calculatedValue = totalValue/totalControl;
        return calculatedValue;
    }

    //Control is decided by the: number of provinces we control bordering it
    //Number of ships we have in it
    [Header("Ocean Province Tax Control")]
    public Dictionary<string, float> oceanProvinceControl = new Dictionary<string, float>(); //(nationIdx,provinceIdx), control number
    float oceanControlFromBorderingLandProvince = 1f;
    float oceanControlFromTradeShips = 0.2f;

    public void SetOceanProvinceControl()
    {
        oceanProvinceControl = new Dictionary<string, float>();
        for(int i = 0; i < gameData.localCopy.nationList.Count; i++)
        {
            GetControllingProvinces(i);
        }
    }

    public void GetControllingProvinces(int nationIdx)
    {
        var ourNation = gameData.localCopy.nationList[nationIdx];
        List<int> provinceIdicesWeControl = GetOurProvinces(ourNation);

        foreach(int provinceIdxControlling in provinceIdicesWeControl)
        {
            ProvinceList province = gameData.localCopy.provinceList[provinceIdxControlling];
            if(province.oceanNeighbours == null || province.oceanNeighbours.Count == 0) continue;

            foreach(int oceanNeighbourIdx in province.oceanNeighbours)
            {
                string keyStr = $"{nationIdx.ToString()},{oceanNeighbourIdx.ToString()}";
                if(!oceanProvinceControl.ContainsKey(keyStr)){
                    oceanProvinceControl.Add(keyStr, oceanControlFromBorderingLandProvince);
                }
                else
                    oceanProvinceControl[keyStr] = oceanProvinceControl[keyStr] + oceanControlFromBorderingLandProvince;
            }


        }

        foreach(Fleet fleet in ourNation.fleets)
        {
            if (!fleet.inPort)
            {   
                string keyStr = $"{nationIdx.ToString()},{fleet.oceanProvinceLocation.ToString()}";
                if(!oceanProvinceControl.ContainsKey(keyStr))
                    oceanProvinceControl.Add(keyStr, oceanControlFromBorderingLandProvince);
                else
                    oceanProvinceControl[keyStr] = oceanProvinceControl[keyStr] + oceanControlFromTradeShips*fleet.GetNumberOfTradeShips();
            }
        }

    }

    List<int> GetOurProvinces(NationList nation)
    {
        List<int> provinceIdices = new List<int>();
        foreach(GovernedState governedState in nation.governedStates)
        {
            foreach(int provinceIdx in governedState.provincesInState)
            {
                if(!provinceIdices.Contains(provinceIdx))
                provinceIdices.Add(provinceIdx);
            }
        }

        return provinceIdices;
    }
}





[Serializable]
public class NationalTradeLogEntry
{
    public TradeLogType tradeType;
    public int    goodType;
    public string goodName;
    public float  amount;
    public int    provinceFrom;    // Province the good originated from
    public int    provinceTo;      // Province the good ended up in / was sold to
    public float  distanceCost;    // Total distance cost paid on this transaction
    public float  price;           // Unit price paid/received on this transaction
    public float  totalCost;       // price * amount (what actually changed hands)
}

public enum TradeLogType
{
    ImportTrade,   // Player bought goods from a foreign (or domestic) seller probe
    ExportTrade,   // Player sold goods to a foreign (or domestic) buyer probe
    ExcessDump,    // Player's unsold cargo got dumped on a stranded province at a loss
    DesperateBuy   // Player's unmet demand was self-served from local stockpile at a premium
}


public class BestExportClass
{
    public int destinationTradeCenter;
    public List<SegmentClass> path;
}

public struct ProvinceTradeEstimate
{
    public int   tradeCenterProvinceIdx;
    public float basePrice;
    public float distanceCost;
    public float estimatedCost;
}
