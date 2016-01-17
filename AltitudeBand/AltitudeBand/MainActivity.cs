using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Drawing;

using Android.Graphics;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android;
using Android.OS;
using Android.Widget;
using Genetics;
using Genetics.Attributes;
using Android.Runtime;

using Microsoft.Band.Portable;
using Microsoft.Band.Portable.Sensors;
using Microsoft.Band.Portable.Notifications;
using Microsoft.Band.Portable.Tiles;
using Microsoft.Band.Portable.Tiles.Pages;
using Microsoft.Band.Portable.Tiles.Pages.Data;

[assembly: UsesPermission(Manifest.Permission.Bluetooth)]
//[assembly: UsesPermission(Manifest.Permission.WriteExternalStorage)]
[assembly: UsesPermission(Microsoft.Band.BandClientManager.BindBandService)]

namespace AltitudeBand
{
	[Activity (Label = "AltitudeBand", MainLauncher = true, Icon = "@mipmap/icon")]
	public class MainActivity : Activity
	{

		Guid tileId = new Guid("{6be209ae-d8ed-4e61-82ec-a82c02aed224}");
		Guid pageId = new Guid("{fadac1ea-6a2f-4003-bae4-3ce855e135dc}");

		Button addTileButton;
		String AddTileString = "Add Altitude Tile";
		String RemoveTileString = "Remove Altitude Tile";
		bool tileFound = false;

		PageData altitudeContent = new PageData();


		TextView pressure;
		TextView height;
		int count;
		double[] filter = new double[20];

		double zeroAGL;
		int altitudeAGL = 88888;
		int pullAltitude = 4500;
		string units = "Feet";

		bool zeroed = false;
		bool armed = false;
		bool fired = false;


		BandBarometerSensor barometer;

		private void BuildPageData(PageData pageContext) 
		{
			pageContext.PageId = pageId;
			pageContext.PageLayoutIndex = 0;
			pageContext.Data.Add(new TextBlockData {
				ElementId = 1,
				Text = "Pull Alt:"
			});
			pageContext.Data.Add (new TextBlockData {
				ElementId = 2,
				Text = pullAltitude.ToString() + " " + units
			});
			if (altitudeAGL <= 0) 
//			if (altitudeAGL <= 1000) 
			{
				pageContext.Data.Add (new ImageData {
					ElementId = 10,	// blue
					ImageIndex = 1
				});
			} 
			else if (altitudeAGL <= 1) 
//			else if (altitudeAGL <= 2500) 
			{
				pageContext.Data.Add (new ImageData {
					ElementId = 11, // red
					ImageIndex = 1
				});
			} 
			else if (altitudeAGL <= 2) 
//			else if (altitudeAGL <= 3000) 
			{
				pageContext.Data.Add (new ImageData {
					ElementId = 12, // yellow
					ImageIndex = 1
				});
			} 
			else 
			{
				pageContext.Data.Add (new ImageData {
					ElementId = 13, // green
					ImageIndex = 1
				});
			}
			pageContext.Data.Add (new TextBlockData {
				ElementId = 5,
				Text = altitudeAGL.ToString()
			});
		}

		private void OnBarometerChanged(object sender, BandSensorReadingEventArgs<BandBarometerReading> e)
		{
			var pReading = e.SensorReading.AirPressure;
			var tReading = e.SensorReading.Temperature;
			if (count < filter.Length) {
				this.RunOnUiThread (() => {
				height.Text = "zeroing...";
				});
				filter [count] = pReading;
				count++;
				if (filter.Length == count) {
					pReading = 0;
					for (int x = 0; x < filter.Length; x++)
						pReading += filter [x];
					pReading /= filter.Length;
					zeroAGL = (1 - Math.Pow (pReading / 1013.25, 0.190284)) * 145366.45;
					zeroed = true;
					Model.Instance.Client.NotificationManager.VibrateAsync(VibrationType.NotificationOneTone);
				}
				if (tileFound) {
					PageData pageContent = new PageData {
						PageId = pageId,
						PageLayoutIndex = 0,
						Data = {
							new TextBlockData {
								ElementId = 1,
								Text = "Pull Alt:"
							},
							new TextBlockData {
								ElementId = 2,
								Text = "Zeroing..."
							},
							new ImageData {
								ElementId = 10,
								ImageIndex = 1
							},
							new TextBlockData {
								ElementId = 5,
								Text = altitudeAGL.ToString()
							}
						}
					};
					Model.Instance.Client.TileManager.SetTilePageDataAsync (tileId, pageContent);
				}
			} else {
				altitudeAGL = (int)(((1 - Math.Pow (pReading / 1013.25, 0.190284)) * 145366.45) - zeroAGL);
				if (zeroed) 
				{
					if (altitudeAGL <= pullAltitude + 100 && altitudeAGL >= pullAltitude - 100) {
						if (!fired) {
							//vibrate
							Model.Instance.Client.NotificationManager.VibrateAsync(VibrationType.NotificationAlarm);
							fired = true;
						}
					} else {
						fired = false;
					}
				}
				this.RunOnUiThread (() => {
					pressure.Text = string.Format ("{0,6:0} hPa", pReading);
					height.Text = string.Format ("{0,6:0} ft", altitudeAGL);
				});

				if (tileFound) {
					PageData pageContent = new PageData ();
					BuildPageData (pageContent);
					Model.Instance.Client.TileManager.SetTilePageDataAsync (tileId, pageContent);
				}
			}
		}


		protected override void OnCreate (Bundle savedInstanceState)
		{
			base.OnCreate (savedInstanceState);
			// Set our view from the "main" layout resource
			SetContentView (Resource.Layout.Main);
			// Get our button from the layout resource,
			// and attach an event to it
			Button connectButton = FindViewById<Button> (Resource.Id.connectButton);
			pressure = FindViewById<TextView> (Resource.Id.pressure);
			height = FindViewById<TextView> (Resource.Id.height);
			addTileButton = FindViewById<Button> (Resource.Id.addTileButton);
			addTileButton.Click += AddTileButton_Click;
			addTileButton.Enabled = false;

			connectButton.Click += async delegate {
				if (Model.Instance.Connected)
				{
					try
					{
						await barometer.StopReadingsAsync();
						await Model.Instance.Client.DisconnectAsync();
						Model.Instance.Client = null;
						pressure.Text = "";
						connectButton.Text = "Connect Band!";
						addTileButton.Enabled = false;
					}
					catch (Exception ex)
					{
						Util.ShowExceptionAlert(this, "Disconnect", ex);
					}
				}
				else
				{
					try
					{
						var bandClientManager = BandClientManager.Instance;
						// query the service for paired devices
						var pairedBands = await bandClientManager.GetPairedBandsAsync();
						// connect to the first device
						var bandInfo = pairedBands.FirstOrDefault();
						var bandClient = await bandClientManager.ConnectAsync(bandInfo);
						Model.Instance.Client = bandClient;
						// get the current set of tiles
						IEnumerable<BandTile> tiles = await Model.Instance.Client.TileManager.GetTilesAsync();
						// get the number of tiles we can add
						int capacity = await Model.Instance.Client.TileManager.GetRemainingTileCapacityAsync();
						foreach(BandTile tile in tiles)
						{
							if (tile.Id.Equals(tileId))
							{
								tileFound = true;
							}
						}
						if (tileFound)
						{
							addTileButton.Text = RemoveTileString;
							addTileButton.Enabled = true;
						}
						else if (capacity != 0)
						{
							addTileButton.Text = AddTileString;
							addTileButton.Enabled = true;
						}
							
						// get the barometer sensor
						barometer = bandClient.SensorManager.Barometer;
						// add a handler
						barometer.ReadingChanged += OnBarometerChanged;
						// zero AGL
						count = 0;
						await barometer.StartReadingsAsync(BandSensorSampleRate.Ms128);
						connectButton.Text = "Disconnect Band!";
						addTileButton.Enabled = true;
						altitudeContent.PageId = pageId;
						altitudeContent.PageLayoutIndex = 1;
					}
					catch (Exception ex)
					{
						Util.ShowExceptionAlert(this, "Connect", ex);
					}
				}
			};
		}

		async void AddTileButton_Click (object sender, EventArgs e)
		{
			if (0 == String.Compare(addTileButton.Text, AddTileString))
			{ // add tile
				try
				{
					BitmapFactory.Options options = new BitmapFactory.Options();
					options.InScaled = false;
					var tile = new BandTile(tileId) {
						Icon = BandImage.FromBitmap(BitmapFactory.DecodeResource(Resources, Resource.Raw.tile, options)),
						Name = "Altitude",
						SmallIcon = BandImage.FromBitmap(BitmapFactory.DecodeResource(Resources, Resource.Raw.badge, options)),
						IsScreenTimeoutDisabled = true
					};
					// define the page layout
					var pageLayout = new PageLayout {
						Root = new FlowPanel {
							Orientation = FlowPanelOrientation.Vertical,
							Rect = new PageRect(0, 0, 258, 128),
						}
					};

					// Page1 line1
					var line1 = new FlowPanel { 
						Orientation = FlowPanelOrientation.Horizontal,
						Rect = new PageRect(0, 0, 258, 30),
						Elements = {
							new TextBlock {
								ElementId = 1,
								Rect = new PageRect(0, 0, 258, 30),
								Margins = new Margins(15,0,0,0),
								VerticalAlignment = VerticalAlignment.Bottom,
								Font = TextBlockFont.Small,
								ColorSource = ElementColorSource.BandHighlight,
							},
							new TextBlock {
								ElementId = 2,
								Rect = new PageRect(0, 0, 258, 30),
								Margins = new Margins(10,0,0,0),
								VerticalAlignment = VerticalAlignment.Bottom,
								Font = TextBlockFont.Small
							}
						}
					};

					// Page1 Line2
					var line2 = new FlowPanel {
						Orientation = FlowPanelOrientation.Horizontal,
						Rect = new PageRect(0,38,280,90),
						Elements = {
							new Icon {
								ElementId = 10,
								Rect = new PageRect(0,0,24,24),
								Margins = new Margins(15,35,0,0),
								VerticalAlignment = VerticalAlignment.Top,
								ColorSource = ElementColorSource.BandBase
							},
							new Icon {
								ElementId = 11,
								Rect = new PageRect(0,0,24,24),
								Margins = new Margins(-24,35,0,0),
								VerticalAlignment = VerticalAlignment.Top,
								Color = new BandColor(0xff,0,0)		//red
							},
							new Icon {
								ElementId = 12,
								Rect = new PageRect(0,0,24,24),
								Margins = new Margins(-24,35,0,0),
								VerticalAlignment = VerticalAlignment.Bottom,
								Color = new BandColor(0xff,0xff,0)	//yellow
							},
							new Icon {
								ElementId = 13,
								Rect = new PageRect(0,0,24,24),
								Margins = new Margins(-24,35,0,0),
								VerticalAlignment = VerticalAlignment.Bottom,
								Color = new BandColor(0,0xff,0)		//green
							},
							new TextBlock {
								ElementId = 5,
								Rect = new PageRect(0, 0, 228, 90),
								Margins = new Margins(10,0,0,15),
								VerticalAlignment = VerticalAlignment.Bottom,
								Font = TextBlockFont.ExtraLargeNumbersBold
							}
						}
					};



					pageLayout.Root.Elements.Add(line1);
					pageLayout.Root.Elements.Add(line2);

					// add the page layout to the tile
					tile.PageLayouts.Add(pageLayout);
					// add the tile to the Band
					await Model.Instance.Client.TileManager.AddTileAsync(tile);
					addTileButton.Text = RemoveTileString;
				}
				catch (Exception ex)
				{
					Util.ShowExceptionAlert(this, "Add Tile", ex);
				}
			}
			else
			{ // remove tile
				try
				{
					await Model.Instance.Client.TileManager.RemoveTileAsync(tileId);
					addTileButton.Text = AddTileString;
				}
				catch (Exception ex)
				{
					Util.ShowExceptionAlert(this, "Remove Tile", ex);
				}
			}

		}

		protected override void OnDestroy()
		{
			try
			{
				if (Model.Instance.Connected)
				{
					Model.Instance.Client.DisconnectAsync();
				}
			}
			catch (Exception ex)
			{
				// ignore failures here
				Console.WriteLine("Error disconnecting: " + ex);
			}
			finally
			{
				Model.Instance.Client = null;
			}

			base.OnPause();
		}
	}
}
