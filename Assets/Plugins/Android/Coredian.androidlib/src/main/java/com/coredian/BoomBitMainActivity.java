package com.coredian;

import android.content.Intent;
import android.os.Bundle;
import android.util.Log;
import com.unity3d.player.UnityPlayer;
import com.unity3d.player.UnityPlayerActivity;
import java.lang.reflect.Field;
import java.util.Arrays;
import java.util.List;

public class BoomBitMainActivity extends UnityPlayerActivity implements IBoomBitMainActivity
{
	private static final String TAG = "BoombitMainActivity";
	private Bundle intentExtras;
	
	protected void onCreate(Bundle savedInstanceState)
	{
		super.onCreate(savedInstanceState);
	}
	
	protected void onDestroy()
	{
		super.onDestroy();
	}
	
	protected void onResume()
	{
		super.onResume();
		
		intentExtras = getIntent().getExtras();
	}
	
	protected void onPause()
	{
		super.onPause();
	}
	
	protected void onNewIntent(Intent intent)
	{
		super.onNewIntent(intent);
	}
	
	public Bundle getIntentExtras()
	{
		return intentExtras;
	}
	
	public UnityPlayer getUnityPlayer()
	{
		List<Field> unityFields = Arrays.asList(BoomBitMainActivity.class.getSuperclass().getDeclaredFields());
		Field unityPlayerField = unityFields.stream().filter(field -> field.getName().equals("mUnityPlayer")).findFirst().orElse(null);
		
		if (unityPlayerField == null)
		{
			Log.e(TAG, "BoomBitMainActivity::getUnityPlayer Couldn't find field mUnityPlayer, returning null.");
			return null;
		}
		
		unityPlayerField.setAccessible(true);
		UnityPlayer unityPlayer = null;
		try {
			unityPlayer = (UnityPlayer) unityPlayerField.get(this);
		}
		catch (Exception ignored) {}
		
		return unityPlayer;
	}
}
