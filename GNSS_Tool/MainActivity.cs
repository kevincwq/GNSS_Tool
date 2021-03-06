﻿using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Support.Design.Widget;
using Android.Support.V4.App;
using Android.Support.V4.Content;
using Android.Support.V7.App;
using Android.Text.Method;
using Android.Views;
using Android.Widget;
using NLog;
using NmeaParser.Nmea;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GNSS_Tool
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        private static readonly ILogger LogD = LogManager.GetCurrentClassLogger();
        private static readonly ILogger LogNmea = LogManager.GetLogger("NMEA");

        private Dictionary<string, string> devices = new Dictionary<string, string>();
        private Dictionary<string, string> position = new Dictionary<string, string>();

        private NmeaParser.NmeaDevice listener;
        private TextView tvOutput, tvStatus;

        // private bool launched;
        private Android.Bluetooth.BluetoothSocket socket;

        private static NmeaParser.NmeaDevice SystemGPS;

        private FloatingActionButton fab;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            Android.Support.V7.Widget.Toolbar toolbar = FindViewById<Android.Support.V7.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            fab = FindViewById<FloatingActionButton>(Resource.Id.fab);
            fab.Click += FabOnClick;

            tvOutput = FindViewById<TextView>(Resource.Id.output);
            tvStatus = FindViewById<TextView>(Resource.Id.status);
            tvStatus.MovementMethod = new ScrollingMovementMethod();


            devices.Add("System GPS", null);
            var devicePicker = FindViewById<Spinner>(Resource.Id.device_picker);
            Java.Util.UUID SERIAL_UUID = Java.Util.UUID.FromString("00001101-0000-1000-8000-00805F9B34FB");
            var adapter = Android.Bluetooth.BluetoothAdapter.DefaultAdapter;
            if (adapter != null)
            {
                foreach (var d in adapter.BondedDevices.Where(d => { var uuids = d.GetUuids(); return uuids != null && uuids.Any(t => t.Uuid.ToString().Equals("00001101-0000-1000-8000-00805F9B34FB", StringComparison.InvariantCultureIgnoreCase)); }))
                {
                    devices[d.Name + " " + d.Address] = d.Address;
                }
            }
            devicePicker.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, devices.Keys.ToArray());
            devicePicker.SetSelection(0);

            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage) != Permission.Granted)
            {
                ActivityCompat.RequestPermissions(this, new[] { Manifest.Permission.WriteExternalStorage }, 1000);
            }

            LogD.Debug("Started");
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        private void FabOnClick(object sender, EventArgs eventArgs)
        {
            //View view = (View) sender;
            //Snackbar.Make(view, "Replace with your own action", Snackbar.LengthLong)
            //    .SetAction("Action", (Android.Views.View.IOnClickListener)null).Show();

            fab.Enabled = false;
            if (listener?.IsOpen == true)
            {
                Stop();
                fab.SetImageResource(Android.Resource.Drawable.IcMediaPlay);
            }
            else
            {
                Start();
                fab.SetImageResource(Android.Resource.Drawable.IcMediaPause);
            }
            fab.Enabled = true;
        }

        private void Stop()
        {
            if (listener != null)
                listener.MessageReceived -= Listener_MessageReceived;
            socket?.Close();
            socket?.Dispose();
            socket = null;
            listener?.CloseAsync();
            listener = null;
        }

        private async void Start()
        {
            var devicePicker = FindViewById<Spinner>(Resource.Id.device_picker);
            var id = devicePicker.SelectedItem.ToString();
            var btAddress = devices[id];
            if (btAddress == null)
            {
                if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.AccessFineLocation) != Permission.Granted)
                {
                    ActivityCompat.RequestPermissions(this, new[] { Manifest.Permission.AccessFineLocation }, 1000);
                    return;
                }

                if (SystemGPS == null)
                    SystemGPS = new NmeaParser.SystemNmeaDevice();

                listener = SystemGPS;
            }
            else //Bluetooth
            {
                try
                {
                    tvOutput.Text = "Opening bluetooth...";
                    var adapter = Android.Bluetooth.BluetoothAdapter.DefaultAdapter;
                    var bt = Android.Bluetooth.BluetoothAdapter.DefaultAdapter.GetRemoteDevice(btAddress);
                    Java.Util.UUID SERIAL_UUID = Java.Util.UUID.FromString("00001101-0000-1000-8000-00805F9B34FB"); //UUID for Serial Device Service
                    socket = bt.CreateRfcommSocketToServiceRecord(SERIAL_UUID);
                    try
                    {
                        await socket.ConnectAsync();
                    }
                    catch (Java.IO.IOException)
                    {
                        // This sometimes fails. Use fallback approach to open socket
                        // Based on https://stackoverflow.com/a/41627149
                        socket.Dispose();
                        var m = bt.Class.GetMethod("createRfcommSocket", new Java.Lang.Class[] { Java.Lang.Integer.Type });
                        socket = m.Invoke(bt, new Java.Lang.Object[] { 1 }) as Android.Bluetooth.BluetoothSocket;
                        socket.Connect();
                    }
                    listener = new NmeaParser.StreamDevice(socket.InputStream);
                }
                catch (System.Exception ex)
                {
                    socket?.Dispose();
                    socket = null;
                    tvOutput.Text += "\nError opening Bluetooth device:\n" + ex.Message;
                    LogD.Error(ex);
                }
            }

            if (listener != null)
            {
                listener.MessageReceived += Listener_MessageReceived;
                tvOutput.Text += "\nOpening device...";
                await listener.OpenAsync();
                tvOutput.Text += "\nConnected!";
            }
        }


        protected override void OnDestroy()
        {
            Stop();
            base.OnDestroy();
        }

        protected override void OnResume()
        {
            base.OnResume();
            // if it was resumed by the GPS permissions dialog
            //Start();
        }


        private void Listener_MessageReceived(object sender, NmeaParser.NmeaMessageReceivedEventArgs e)
        {
            var message = e.Message;
            var str = message.ToString();
            LogNmea.Info(str);

            RunOnUiThread(() =>
            {
                tvOutput.Append($"\n{str}");
                if (message is Gga gga)
                {
                    position["Lat"] = gga.Latitude.ToString("F8");
                    position["Lon"] = gga.Longitude.ToString("F8");
                    position["Alt"] = gga.Altitude.ToString() + " " + gga.AltitudeUnits.ToString();
                    // position["Height"] = gga.HeightOfGeoid.ToString() + " " + gga.HeightOfGeoidUnits.ToString();
                    position["Quality"] = gga.Quality.ToString();
                    position["Satellites"] = gga.NumberOfSatellites.ToString();
                    position["StationID"] = gga.DgpsStationId.ToString();
                    position["HDOP"] = gga.Hdop.ToString();
                }
                else if (message is Rmc rmc)
                {
                    position["Lat"] = rmc.Latitude.ToString("F8");
                    position["Lon"] = rmc.Longitude.ToString("F8");
                    position["Speed"] = rmc.Speed.ToString("F2");
                }
                tvStatus.Text = string.Join('\n', position.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key.PadRight(15, ' ')}:  {kv.Value}"));
            });
        }
    }
}

