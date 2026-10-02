using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Linq;

public class SmartTradeUI : MonoBehaviour
{
    [Header("Script Reference")]
    public MapData mapData;
    public GameData gameData;
    public SmartTradePath smartTradePath;
    public Charachters charachters;
    [SerializeField] private TradeSystem tradeSystem;


    [Header("Passage Bans")]
    [SerializeField] private Transform passageBanParent;
    [SerializeField] private GameObject passageBanPrefab;
    List<passageMicro> referredPM = new List<passageMicro>();


    [Header("Passage Tax")]
    [SerializeField] private Transform passageTaxParent;
    [SerializeField] private GameObject passageTaxPrefab;
    List<passageTaxMicro> referredPTM = new List<passageTaxMicro>();


    [Header("Tariffs")]
    [SerializeField] private Transform tariffParent;
    [SerializeField] private GameObject tariffPrefab;
    [SerializeField] private TMP_InputField tariffSearchField;


    [Header("Excess Utils")]
    public Color selectedColor;
    public Color unselectedColor;

    public void OpenTariffs()
    {
        charachters.DestroyAllChildren(tariffParent);

        for(int i = 0; i < gameData.localCopy.nationList.Count; i++)
        {
            if (i == mapData.playerNation) continue;

            GameObject newTariffObject = Instantiate(tariffPrefab);
            newTariffObject.transform.SetParent(tariffParent);
            newTariffObject.transform.localScale = new Vector3(1f,1f,1f);

            newTariffObject.transform.Find("NameTXT").GetComponent<TMP_Text>().text = "";
        }
    }    

    public void OpenPassageBans()
    {
        charachters.DestroyAllChildren(passageBanParent);
 
        for (int i = 0; i < gameData.localCopy.nationList.Count; i++)
        {
            GameObject newPassageBan = Instantiate(passageBanPrefab);
            newPassageBan.transform.SetParent(passageBanParent);
            newPassageBan.transform.localScale = new Vector3(1f,1f,1f);
            newPassageBan.transform.GetChild(1).transform.GetComponent<TMP_Text>().text = gameData.localCopy.nationList[i].name;
            newPassageBan.transform.GetChild(3).GetChild(0).gameObject.SetActive(false);

            passageMicro pM = newPassageBan.transform.GetComponent<passageMicro>();
            pM.reference = this;
            pM.myIndex = i;
            pM.isBanned = gameData.localCopy.nationList[mapData.playerNation].nationsBanned.Contains(i);
            referredPM.Add(pM);
        }
    }

    public void OpenPassageTax()
    {
        charachters.DestroyAllChildren(passageTaxParent);
        var ourNation = gameData.localCopy.nationList[mapData.playerNation];
        
        List<int> ourProvinces = GetOurProvinces(ourNation);
        foreach(int provinceIdx in ourProvinces)
        {
            var province = gameData.localCopy.provinceList[provinceIdx];

            GameObject newPasageTax = Instantiate(passageTaxPrefab);
            newPasageTax.transform.SetParent(passageTaxParent);
            newPasageTax.transform.Find("NameTXT").GetComponent<TMP_Text>().text = province.name;
            newPasageTax.transform.Find("LandOceanTXT").GetComponent<TMP_Text>().text = "Land";
            newPasageTax.transform.Find("RateField").GetComponent<TMP_InputField>().SetTextWithoutNotify(province.passageTax.ToString());
            newPasageTax.transform.localScale = new Vector3(1f,1f,1f);

            var thisProvince = gameData.localCopy.provinceList[provinceIdx];
            float taxPaidHere = thisProvince.passageTax;

            passageTaxMicro pTM = newPasageTax.transform.GetComponent<passageTaxMicro>();
            pTM.reference = this;
            pTM.myIndex = provinceIdx;
            pTM.myPTMIndex = referredPTM.Count;
            pTM.tax = taxPaidHere;
            pTM.isOceanProvince = false;

            referredPTM.Add(pTM);
        } 


        //Now ocean provinces we have control over 
        for(int i = 0; i < gameData.localCopy.oceanProvinces.Count; i++)
        {
            string constructedKey = $"{mapData.playerNation.ToString()},{i.ToString()}";
            if(!tradeSystem.oceanProvinceControl.ContainsKey(constructedKey)) continue;

            var oceanProvince = gameData.localCopy.oceanProvinces[i];
            OceanTradeTax oceanTradeTax = gameData.localCopy.oceanTradeTaxes[i];
            NationalOTT nationalOTT = oceanTradeTax.nationOceanTradeTaxes.FirstOrDefault(n => n.nationIndex == mapData.playerNation);
            if(nationalOTT == null) {
                nationalOTT = new NationalOTT { nationIndex = mapData.playerNation, nationControl = 1, nationTax = 0 };
                oceanTradeTax.nationOceanTradeTaxes.Add(nationalOTT);
            }


            GameObject newOceanPassageTaxOb = Instantiate(passageTaxPrefab);
            newOceanPassageTaxOb.transform.SetParent(passageTaxParent);
            newOceanPassageTaxOb.transform.Find("NameTXT").GetComponent<TMP_Text>().text = oceanProvince.name;
            newOceanPassageTaxOb.transform.Find("LandOceanTXT").GetComponent<TMP_Text>().text = "Ocean";
            newOceanPassageTaxOb.transform.Find("RateField").GetComponent<TMP_InputField>().SetTextWithoutNotify(nationalOTT.nationTax.ToString());
            newOceanPassageTaxOb.transform.localScale = new Vector3(1f,1f,1f);

            var thisProvince = gameData.localCopy.provinceList[i];
            float taxPaidHere = oceanProvince.passageTax;

            passageTaxMicro pTM = newOceanPassageTaxOb.transform.GetComponent<passageTaxMicro>();
            pTM.reference = this;
            pTM.myIndex = i;
            pTM.myPTMIndex = referredPTM.Count;
            pTM.tax = taxPaidHere;
            pTM.isOceanProvince = true;

            referredPTM.Add(pTM);

        }
    }






    public List<int> GetOurProvinces(NationList nation)
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

    public void SelectAll(bool newState)
    {
        for (int i = 0; i < referredPM.Count; i++)
        {
            referredPM[i].isSelected = newState;
            referredPM[i].OnSelect();
        }
    }

    public void ConfigureBans(int senderIndex)
    {
        for (int i = 0; i < referredPM.Count; i++)
        {
            if (referredPM[i].isSelected || referredPM[i].myIndex == senderIndex)
            {
                referredPM[i].isBanned = referredPM[senderIndex].isBanned; //Flipflopped in class
                referredPM[i].gameObject.transform.GetChild(3).GetChild(0).gameObject.SetActive(referredPM[i].isBanned);

                if (referredPM[i].isBanned)
                {
                    if (!gameData.localCopy.nationList[mapData.playerNation].nationsBanned.Contains(referredPM[i].myIndex))
                        gameData.localCopy.nationList[mapData.playerNation].nationsBanned.Add(referredPM[i].myIndex);
                }
                else if (referredPM[i].isBanned == false)
                {
                    if (gameData.localCopy.nationList[mapData.playerNation].nationsBanned.Contains(referredPM[i].myIndex))
                        gameData.localCopy.nationList[mapData.playerNation].nationsBanned.Remove(referredPM[i].myIndex);
                }

                //And call SmartTradePath
                smartTradePath.ApplyTradeBan(mapData.playerNation, referredPM[i].myIndex, referredPM[i].isBanned);
            }
        }
    }


    /* In this case we allow for multi selection, so we can select one multiple objects at once and then use
    the changes on a single selected object to affect change on all the selected objects at once.
    */
    public void ConfigureTaxes(int senderIndex)
    {
        for (int i = 0; i < referredPTM.Count; i++)
        {
            if (referredPTM[i].isSelected || referredPTM[i].myPTMIndex == senderIndex)
            {

                referredPTM[i].tax = referredPTM[senderIndex].tax; //Flipflopped in class
                //referredPTM[i].gameObject.transform.Find("RateField").GetComponent<TMP_InputField>().text = referredPTM[senderIndex].gameObject.transform.Find("RateField").GetComponent<TMP_InputField>().text;
                var refer = referredPTM[i];

                OceanTradeTax oceanTradeTax = gameData.localCopy.oceanTradeTaxes[refer.myIndex];
                NationalOTT nationalOTT = oceanTradeTax.nationOceanTradeTaxes.FirstOrDefault(n => n.nationIndex == mapData.playerNation);
                if(nationalOTT == null) {
                    nationalOTT = new NationalOTT { nationIndex = mapData.playerNation, nationControl = 1, nationTax = referredPTM[senderIndex].tax };
                    oceanTradeTax.nationOceanTradeTaxes.Add(nationalOTT);
                } else {
                    nationalOTT.nationTax = referredPTM[senderIndex].tax;
                }


                if(refer.isOceanProvince)
                    tradeSystem.SetTradeTaxOfOceanProvince(refer.myIndex);
                else
                gameData.localCopy.provinceList[refer.myIndex].passageTax = referredPTM[senderIndex].tax;

                if(refer.isOceanProvince)
                smartTradePath.ApplyOceanPassageTaxOnSingleProvince(refer.myIndex, referredPTM[i].tax);
                else
                smartTradePath.ApplyPassageTaxOnSingleProvince(refer.myIndex, referredPTM[i].tax);
            }
        }
    }
}
