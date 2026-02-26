using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SaveSystem
{
    private static GlobalSaveData _save;

    public static GlobalSaveData Save
    {
        get
        {
            // 세이브가 없으면 불러오기
            if (_save == null)
                _save = LoadData();

            // 불러오기를 했는데도 없으면 빈 걸 주기
            if (_save == null)
                _save = new GlobalSaveData();
            return _save;
        }
    }
    static string path = Application.persistentDataPath + "/GlobalSaves.json";
    


    public static void SaveData()
    {
        if (_save == null) return;
        _save.Save();
        // Set our save location and make sure we have a saves folder ready to go.

        Debug.Log("Saving ");

        FileStream fileStream = new FileStream(path, FileMode.Create);
        byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(_save));
        fileStream.Write(data, 0, data.Length);
        fileStream.Close();
    }

    public static GlobalSaveData LoadData()
    {
        if (File.Exists(path))
        {
            Debug.Log("loading from save.");

            FileStream fileStream = new FileStream(path, FileMode.Open);
            byte[] data = new byte[fileStream.Length];
            fileStream.Read(data, 0, data.Length);
            fileStream.Close();
            string jsonData = Encoding.UTF8.GetString(data);
            return JsonUtility.FromJson<GlobalSaveData>(jsonData);
        }
        else
        {
            return null;
        }
    }

    public static void DeleteData()
    {
        if (!File.Exists(path))
            return;

        FileInfo deleteDir = new FileInfo(path);
        deleteDir.Delete();
        _save = null;
    }
}
