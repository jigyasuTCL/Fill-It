package com.coredian;

import android.app.Application;
import android.content.Context;
import androidx.multidex.MultiDex;
import com.boombit.sdk.firebase.FCrashlytics;
import com.coredian.privacy.usercentrics.CoreUsercentrics;

public class BoomBitApplication extends Application
{
	public void onCreate()
	{
		super.onCreate();
		
		FCrashlytics.startAnrWatchDog();
		CoreUsercentrics.getInstance().initialize(this);
	}
	
	public void onTerminate()
	{
		super.onTerminate();
	}
	
	protected void attachBaseContext(Context base)
	{
		super.attachBaseContext(base);
		
		MultiDex.install(this);
	}
}
