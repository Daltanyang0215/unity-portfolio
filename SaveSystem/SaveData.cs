using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GlobalSaveData
{
    public int LastSaveIndex;
    
    public int CurSaveIndex = -1;
    public List<SaveData> SaveDatas;
    public SaveData CurSave => CurSaveIndex == -1 ? null : SaveDatas[CurSaveIndex];
    public GlobalSaveData()
    {
        SaveDatas = new List<SaveData>() {null,null,null };
    }
    public void Save()
    {
        CurSave?.Save();
        LastSaveIndex = CurSaveIndex;
    }

    public void SetCurSaveIndex(int index)
    {
        if (SaveDatas[index] == null)
        {
            SaveDatas[index] = new SaveData();
        }

        CurSaveIndex = index;
    }
}
[System.Serializable]
public class SaveData
{
    public string LastTimeToString;
    public int DayCount;
    public List<int> RequesterPoints;
    public int HasCoin;

    public SaveData()
    {
        DayCount = 0;
        HasCoin = 50;
        RequesterPoints = new List<int>();
        for (int i = 0; i < Enum.GetValues(typeof(RequesterType)).Length; i++)
        {
            RequesterPoints.Add(0);
        }
    }

    public void Save()
    {
        LastTimeToString = DateTime.Now.ToString();
    }
}
