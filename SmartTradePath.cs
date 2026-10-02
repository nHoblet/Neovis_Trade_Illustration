using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;

public class SmartTradePath : MonoBehaviour
{
    [Header("Required Variables")]
    public float oceanCost;
    public float landCost;

    [Header("Script References")]
    public MapData  mapData;
    public GameData gameData;

    [Tooltip("Optional. If assigned, bans/tax changes here will also invalidate the trade network's cached legs so the two systems never drift out of sync.")]
    public TradeNetworkPathfinder tradeNetworkPathfinder;

    // ─────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────

    void Start()
    {
        LoadPaths();
    }

    // ─────────────────────────────────────────────
    //  Save / Load
    // ─────────────────────────────────────────────

    void SavePaths() => PathSaveSystem.SavePaths(gameData.localCopy.existingPaths);

    void LoadPaths()
    {
        gameData.localCopy.existingPaths = PathSaveSystem.LoadPaths();

        // Ensure list is sized to province count even if the save was empty or stale
        EnsurePathListSize();
    }

    void EnsurePathListSize()
    {
        if (gameData?.localCopy?.provinceList == null) return;

        int provinceCount = gameData.localCopy.provinceList.Count;

        if (gameData.localCopy.existingPaths == null)
            gameData.localCopy.existingPaths = new List<Path>();

        while (gameData.localCopy.existingPaths.Count < provinceCount)
            gameData.localCopy.existingPaths.Add(new Path { pathsBetweenProvinces = new List<PathCircumstances>() });
    }

    // ─────────────────────────────────────────────
    //  Path lookup  (compute-on-miss, then cache)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Returns the path from startIdx to endIdx for the given nation.
    /// If no compatible cached path exists, computes one with Dijkstra,
    /// stores it, and returns it. Returns null only if no path exists in the graph.
    ///
    /// Cache compatibility is now based ONLY on the nation's bans — passage
    /// taxes are universal per-province (land AND ocean), so they never make
    /// a cached path incompatible between nations (they only affect cost,
    /// handled by InvalidateTaxAffectedPaths whenever a tax changes).
    /// </summary>
    public List<SegmentClass> FetchPathForNation(int startIdx, int endIdx, int nationIdx)
    {
        if (!ValidateState("FetchPathForNation")) return null;
        if (!ValidateNationIndex("FetchPathForNation", nationIdx)) return null;
        if (startIdx < 0 || startIdx >= gameData.localCopy.provinceList.Count) return null;
        if (endIdx   < 0 || endIdx   >= gameData.localCopy.provinceList.Count) return null;
        if (startIdx == endIdx) return new List<SegmentClass>(); // trivial path

        EnsurePathListSize();

        // ── 1. Try cache ──
        var cached = FindCachedPath(startIdx, endIdx, nationIdx);
        if (cached != null) return cached;

        // ── 2. Cache miss — compute on demand ──
        var nation     = gameData.localCopy.nationList[nationIdx];
        var nationBans = nation.provincesBannedFrom ?? new List<int>();
        
        var computed = DistanceSearchDijkstra(startIdx, endIdx, nationIdx);

        // Store result (even if null — callers handle null as "no route")
        // We only cache non-null paths to avoid polluting the cache with unreachable pairs
        if (computed != null && computed.Count > 0)
        {
            var entry = gameData.localCopy.existingPaths[startIdx];
            entry.pathsBetweenProvinces ??= new List<PathCircumstances>();
            entry.pathsBetweenProvinces.Add(new PathCircumstances
            {
                provinceBans   = new List<int>(nationBans),
                associatedPath = computed
            });
        }

        return computed;
    }

    /// <summary>
    /// Looks for a stored path from startIdx to endIdx that is compatible with
    /// the nation's current bans. Returns null on miss. Taxes are not part of
    /// this check — they're universal, so they can't make a path incompatible,
    /// only stale (handled separately via InvalidateTaxAffectedPaths).
    /// </summary>
    List<SegmentClass> FindCachedPath(int startIdx, int endIdx, int nationIdx)
    {
        var paths = gameData.localCopy.existingPaths;
        if (paths == null || startIdx >= paths.Count) return null;

        var pathEntry = paths[startIdx];
        if (pathEntry?.pathsBetweenProvinces == null) return null;

        var nation     = gameData.localCopy.nationList[nationIdx];
        var nationBans = nation.provincesBannedFrom ?? new List<int>();

        foreach (var pc in pathEntry.pathsBetweenProvinces)
        {
            if (pc?.associatedPath == null || pc.associatedPath.Count == 0) continue;

            // Check destination
            var lastSeg = pc.associatedPath[pc.associatedPath.Count - 1];
            if (lastSeg.oceanNotLand || lastSeg.landIndex != endIdx) continue;

            // Check bans: every ban the nation has must be in this path's ban list
            bool compatible = true;
            foreach (int ban in nationBans)
            {
                if (pc.provinceBans == null || !pc.provinceBans.Contains(ban))
                { compatible = false; break; }
            }
            if (!compatible) continue;

            return pc.associatedPath;
        }

        return null;
    }

    // ─────────────────────────────────────────────
    //  Trade ban / tax application
    // ─────────────────────────────────────────────

    /// <summary>
    /// Bans or un-bans a nation from travelling through ourNation's provinces,
    /// then invalidates any affected cached paths belonging to that nation so
    /// they recompute on next fetch. Bans stay per-nation.
    /// </summary>
    public void ApplyTradeBan(int ourNationIndex, int nationIndex, bool isBanned)
    {
        if (!ValidateState("ApplyTradeBan")) return;
        if (!ValidateNationIndex("ApplyTradeBan", nationIndex)) return;

        var nation = gameData.localCopy.nationList[nationIndex];
        nation.provincesBannedFrom ??= new List<int>();

        List<int> ourProvinces = GetProvincesOwnedBy(ourNationIndex);

        foreach (int p in ourProvinces)
        {
            if (isBanned) { if (!nation.provincesBannedFrom.Contains(p)) nation.provincesBannedFrom.Add(p); }
            else            nation.provincesBannedFrom.Remove(p);
        }

        InvalidateBanAffectedPaths(ourProvinces, nationIndex);
        Debug.Log("ApplyTradeBan: stale paths invalidated for this nation; they will recompute on next fetch.");
    }

    /// <summary>
    /// Sets a passage tax on ourNationIndex's LAND provinces. This tax applies
    /// universally — every nation passing through pays the same rate, since
    /// the tax lives on the province, not on any nation's data.
    /// </summary>
    public void ApplyPassageTax(int ourNationIndex, float tax)
    {
        if (!ValidateState("ApplyPassageTax")) return;

        List<int> ourProvinces = GetProvincesOwnedBy(ourNationIndex);

        foreach (int p in ourProvinces)
            gameData.localCopy.provinceList[p].passageTax = tax;

        InvalidateTaxAffectedPaths(ourProvinces, null);
        Debug.Log("ApplyPassageTax: stale paths invalidated for ALL nations; they will recompute on next fetch.");
    }

    public void ApplyPassageTaxOnSingleProvince(int provinceIdx, float tax)
    {
        if (!ValidateState("ApplyPassageTax")) return;

        gameData.localCopy.provinceList[provinceIdx].passageTax = tax;

        InvalidateTaxAffectedPaths(new List<int> { provinceIdx }, null);
        Debug.Log("ApplyPassageTax: stale paths invalidated for ALL nations; they will recompute on next fetch.");
    }

    public void ApplyOceanPassageTaxOnSingleProvince(int oceanProvinceIdx, float tax)
    {
        if (!ValidateState("ApplyOceanPassageTax")) return;

        gameData.localCopy.oceanProvinces[oceanProvinceIdx].passageTax = tax;

        InvalidateTaxAffectedPaths(null, new List<int> { oceanProvinceIdx });
        Debug.Log("ApplyOceanPassageTax: stale paths invalidated for ALL nations; they will recompute on next fetch.");
    }

    /// <summary>
    /// Drops cached path entries — for the given nation only — that pass
    /// through any of the affected provinces. Used when bans change, since
    /// bans are nation-specific. They will be recomputed lazily on next
    /// FetchPathForNation call.
    /// </summary>
    void InvalidateBanAffectedPaths(List<int> affectedProvinces, int nationIdx)
    {
        if (gameData.localCopy.existingPaths == null) return;

        var nation     = gameData.localCopy.nationList[nationIdx];
        var nationBans = nation.provincesBannedFrom ?? new List<int>();

        foreach (var pathGroup in gameData.localCopy.existingPaths)
        {
            if (pathGroup?.pathsBetweenProvinces == null) continue;

            for (int y = pathGroup.pathsBetweenProvinces.Count - 1; y >= 0; y--)
            {
                var pc = pathGroup.pathsBetweenProvinces[y];
                if (pc?.associatedPath == null) continue;

                // Only invalidate entries that belong to this nation's ban circumstance
                bool isThisNation = NationBansMatch(pc.provinceBans, nationBans);
                if (!isThisNation) continue;

                bool affected = false;
                foreach (var seg in pc.associatedPath)
                {
                    if (!seg.oceanNotLand && affectedProvinces.Contains(seg.landIndex))
                    { affected = true; break; }
                }

                if (affected)
                    pathGroup.pathsBetweenProvinces.RemoveAt(y);
                // No recompute here — FetchPathForNation handles that on next call
            }
        }
    }

    /// <summary>
    /// Drops cached path entries for ALL nations that pass through any of the
    /// affected land or ocean provinces. Used when a tax changes (land or
    /// ocean), since taxes are universal and can shift the optimal route for
    /// every nation, not just one. Pass null (or an empty list) for whichever
    /// province type wasn't affected.
    /// </summary>
    void InvalidateTaxAffectedPaths(List<int> affectedLandProvinces, List<int> affectedOceanProvinces)
    {
        if (gameData.localCopy.existingPaths == null) return;

        bool hasLand  = affectedLandProvinces  != null && affectedLandProvinces.Count  > 0;
        bool hasOcean = affectedOceanProvinces != null && affectedOceanProvinces.Count > 0;

        if (!hasLand && !hasOcean) return;

        foreach (var pathGroup in gameData.localCopy.existingPaths)
        {
            if (pathGroup?.pathsBetweenProvinces == null) continue;

            for (int y = pathGroup.pathsBetweenProvinces.Count - 1; y >= 0; y--)
            {
                var pc = pathGroup.pathsBetweenProvinces[y];
                if (pc?.associatedPath == null) continue;

                bool affected = false;
                foreach (var seg in pc.associatedPath)
                {
                    if (!seg.oceanNotLand && hasLand && affectedLandProvinces.Contains(seg.landIndex))
                    { affected = true; break; }

                    if (seg.oceanNotLand && hasOcean && affectedOceanProvinces.Contains(seg.oceanIndex))
                    { affected = true; break; }
                }

                if (affected)
                    pathGroup.pathsBetweenProvinces.RemoveAt(y);
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Dijkstra
    // ─────────────────────────────────────────────

   public List<SegmentClass> DistanceSearchDijkstra(int startProvince, int destinationProvince,
                                                 int nationIdx, bool startIsOcean = false)
{
    var pq        = new SimplePriorityQueue<SegmentClass>();
    var costSoFar = new Dictionary<SegmentClass, float>();
    var cameFrom  = new Dictionary<SegmentClass, SegmentClass>();
    var closed    = new HashSet<SegmentClass>();

    SegmentClass startSeg = new SegmentClass
    {
        oceanNotLand = startIsOcean,
        landIndex    = startIsOcean ? -1 : startProvince,
        oceanIndex   = startIsOcean ? startProvince : -1,
        distanceCost = 0f
    };

    pq.Enqueue(startSeg, 0f);
    costSoFar[startSeg] = 0f;
    cameFrom[startSeg]  = null;

    // Bans are still nation-specific — who is blocked from a province.
    var bannedProvinces = FetchBannedProvinces(nationIdx);

    while (pq.Count > 0)
    {
        pq.TryDequeue(out SegmentClass current, out float currentCost);

        if (closed.Contains(current)) continue;
        if (costSoFar.TryGetValue(current, out float bestKnown) && currentCost > bestKnown) continue;

        closed.Add(current);

        bool atDest = current.oceanNotLand
            ? current.oceanIndex == destinationProvince
            : current.landIndex  == destinationProvince;

        if (atDest)
            return ReconstructPath(cameFrom, current);

        foreach (SegmentClass neighbor in GetNeighbours(current))
        {
            int idx = neighbor.oceanNotLand ? neighbor.oceanIndex : neighbor.landIndex;
            if (bannedProvinces.Contains(idx)) continue;
            if (closed.Contains(neighbor)) continue;

            float moveCost = GetDistanceCost(neighbor);
            // Taxes are universal per-province (land AND ocean) — read
            // straight off the province, same value regardless of nation.
            float fee = GetProvincePassageFee(neighbor);
            float newCost = costSoFar[current] + moveCost + fee;

            if (!costSoFar.TryGetValue(neighbor, out float existing) || newCost < existing)
            {
                costSoFar[neighbor]   = newCost;
                neighbor.distanceCost = newCost;
                neighbor.passageFee   = fee;
                cameFrom[neighbor]    = current;
                pq.Enqueue(neighbor, newCost);
            }
        }
    }

    return null; // no path found
}

    static List<SegmentClass> ReconstructPath(Dictionary<SegmentClass, SegmentClass> cameFrom, SegmentClass end)
    {
        var path = new List<SegmentClass>();
        for (SegmentClass p = end; p != null; p = cameFrom[p])
            path.Add(p);
        path.Reverse();
        return path;
    }

    // ─────────────────────────────────────────────
    //  Nation helper queries
    // ─────────────────────────────────────────────

    public List<int> FetchBannedProvinces(int nationIdx)
    {
        if (nationIdx < 0 || nationIdx >= gameData.localCopy.nationList.Count) return new List<int>();
        return gameData.localCopy.nationList[nationIdx].provincesBannedFrom ?? new List<int>();
    }

    /// <summary>
    /// Universal passage fee for a segment — land provinces and ocean
    /// provinces each carry their own `passageTax`, both now taxable.
    /// </summary>
    float GetProvincePassageFee(SegmentClass segment)
    {
        return segment.oceanNotLand
            ? GetOceanPassageFee(segment.oceanIndex)
            : GetProvincePassageFee(segment.landIndex);
    }

    /// <summary>
    /// Universal passage fee for a land province — same value for every nation.
    /// Public so other systems that reuse this graph's cost model (e.g.
    /// TradeNetworkPathfinder) can read it without duplicating the lookup.
    /// </summary>
    public float GetProvincePassageFee(int landProvinceIndex)
    {
        var provinces = gameData.localCopy.provinceList;
        if (landProvinceIndex < 0 || landProvinceIndex >= provinces.Count) return 0f;
        return provinces[landProvinceIndex].passageTax;
    }

    /// <summary>
    /// Universal passage fee for an ocean province — same value for every
    /// nation passing through, mirroring GetProvincePassageFee(int) for land.
    /// Public for the same reuse reasons as its land counterpart.
    /// </summary>
    public float GetOceanPassageFee(int oceanProvinceIndex)
    {
        var oceans = gameData.localCopy.oceanProvinces;
        if (oceans == null || oceanProvinceIndex < 0 || oceanProvinceIndex >= oceans.Count) return 0f;
        return oceans[oceanProvinceIndex].passageTax;
    }

    // ─────────────────────────────────────────────
    //  Graph helpers
    // ─────────────────────────────────────────────

    float GetDistanceCost(SegmentClass to) => to.oceanNotLand ? oceanCost : landCost;

    public List<SegmentClass> GetNeighbours(SegmentClass origin)
    {
        var list = new List<SegmentClass>();

        if (origin.oceanNotLand)
        {
            var oProvince = gameData.localCopy.oceanProvinces[origin.oceanIndex];
            foreach (int n in oProvince.oceanNeighbors)
                list.Add(new SegmentClass { oceanNotLand = true,  oceanIndex = n, landIndex = -1 });
            foreach (int n in oProvince.touchingLandProvinces)
                list.Add(new SegmentClass { oceanNotLand = false, landIndex  = n, oceanIndex = -1 });
        }
        else
        {
            var province = gameData.localCopy.provinceList[origin.landIndex];
            foreach (int n in province.neighbours)
                list.Add(new SegmentClass { oceanNotLand = false, landIndex  = n, oceanIndex = -1 });
            foreach (int n in province.oceanNeighbours)
                list.Add(new SegmentClass { oceanNotLand = true,  oceanIndex = n, landIndex = -1 });
        }

        return list;
    }

    // ─────────────────────────────────────────────
    //  Utility
    // ─────────────────────────────────────────────

    List<int> GetProvincesOwnedBy(int nationIndex)
    {
        var result = new List<int>();
        for (int i = 0; i < gameData.localCopy.provinceList.Count; i++)
        {
            var prov = gameData.localCopy.provinceList[i];
            if (prov == null) continue;
            if (int.TryParse(prov.owner, out int owner) && owner == nationIndex)
                result.Add(i);
        }
        return result;
    }


    bool ValidateState(string caller)
    {
        if (mapData == null || gameData?.localCopy?.provinceList == null || gameData.localCopy.nationList == null)
        {
            Debug.LogWarning($"{caller}: mapData or game lists are null.");
            return false;
        }
        return true;
    }

    bool ValidateNationIndex(string caller, int idx)
    {
        if (idx < 0 || idx >= gameData.localCopy.nationList.Count)
        {
            Debug.LogWarning($"{caller}: Invalid nationIndex {idx}.");
            return false;
        }
        return true;
    }

    // Checks whether two ban lists contain the same entries (order-insensitive)
    static bool NationBansMatch(List<int> a, List<int> b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;
        foreach (int x in b) if (!a.Contains(x)) return false;
        return true;
    }

    // ─────────────────────────────────────────────
    //  Optional: persist cache at end of session
    // ─────────────────────────────────────────────

    /// <summary>
    /// Call this on application quit or scene unload to persist the
    /// lazily-built path cache so it survives between sessions.
    /// </summary>
    public void PersistCache() => SavePaths();

    void OnApplicationQuit() => PersistCache();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Data classes
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public class Path
{
    public List<PathCircumstances> pathsBetweenProvinces;
}

[Serializable]
public class PathCircumstances
{
    // Bans stay per-nation, so cache compatibility still keys off this.
    // Taxes were removed here: they're universal now (land AND ocean) and
    // never affect cache *compatibility*, only cache *staleness*
    // (see InvalidateTaxAffectedPaths).
    public List<int>          provinceBans;
    public List<SegmentClass> associatedPath;
}

[Serializable]
public class SegmentClass
{
    public bool  oceanNotLand;
    public int   oceanIndex   = -1;
    public int   landIndex    = -1;
    public float distanceCost = 0f;
    public float passageFee   = 0f;

    public override bool Equals(object obj)
    {
        if (obj is SegmentClass o)
            return oceanNotLand == o.oceanNotLand &&
                   oceanIndex   == o.oceanIndex   &&
                   landIndex    == o.landIndex;
        return false;
    }

    public override int GetHashCode() => HashCode.Combine(oceanNotLand, oceanIndex, landIndex);
}

[Serializable]
public class PathListWrapper
{
    public List<Path> allPaths = new();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Save system  (unchanged)
// ─────────────────────────────────────────────────────────────────────────────

public static class PathSaveSystem
{
    static string SavePath => Application.streamingAssetsPath + "/paths.json";

    public static void SavePaths(List<Path> paths)
    {
        var wrapper = new PathListWrapper { allPaths = paths };
        File.WriteAllText(SavePath, JsonUtility.ToJson(wrapper, true));
        Debug.Log($"Paths saved to {SavePath}");
    }

    public static List<Path> LoadPaths()
    {
        if (!File.Exists(SavePath))
        {
            Debug.LogWarning("No saved paths found — starting with empty cache.");
            return new List<Path>();
        }

        var wrapper = JsonUtility.FromJson<PathListWrapper>(File.ReadAllText(SavePath));
        return wrapper?.allPaths ?? new List<Path>();
    }

    public static void DeleteSave()
    {
        if (File.Exists(SavePath))
        {
            File.Delete(SavePath);
            Debug.Log("Saved paths deleted.");
        }
    }
}

public class SimplePriorityQueue<TElement>
{
    private readonly List<(TElement item, float priority)> heap = new();

    public int Count => heap.Count;

    public void Enqueue(TElement item, float priority)
    {
        heap.Add((item, priority));
        int i = heap.Count - 1;

        // Bubble up
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (heap[parent].priority <= heap[i].priority) break;
            (heap[parent], heap[i]) = (heap[i], heap[parent]);
            i = parent;
        }
    }

    public bool TryDequeue(out TElement item, out float priority)
    {
        if (heap.Count == 0)
        {
            item = default;
            priority = default;
            return false;
        }

        (item, priority) = heap[0];

        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);

        // Bubble down
        int i = 0;
        int count = heap.Count;
        while (true)
        {
            int left  = i * 2 + 1;
            int right = i * 2 + 2;
            int smallest = i;

            if (left  < count && heap[left].priority  < heap[smallest].priority) smallest = left;
            if (right < count && heap[right].priority < heap[smallest].priority) smallest = right;

            if (smallest == i) break;

            (heap[i], heap[smallest]) = (heap[smallest], heap[i]);
            i = smallest;
        }

        return true;
    }
}