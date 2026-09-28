using UnityEngine;
using System.Collections.Generic;

public class AudioSceneData : MonoBehaviour
{
    [SerializeField]
    private List<SoundData> soundDataList;
    
    [SerializeField]
    private List<SoundData> musicSoundDataList;

    void Awake()
    {
        AudioManager.Instance.AddSoundSceneData(this);
    }

    private void OnDestroy()
    {
        if (AudioManager.IsInstanceExist())
            AudioManager.Instance.RemoveSoundSceneData(this);
    }

    public SoundData GetSoundData(string clipName)
    {
        return soundDataList.Find(x=> x.name == clipName);
    }
    
    public SoundData GetMusicData(string clipName)
    {
        return musicSoundDataList.Find(x=> x.name == clipName);
    }
}