﻿using NUnit.Framework;
using UITest.Appium;

namespace UITests
{
    public class Issue2681 : IssuesUITest
	{
		const string NavigateToPage = "Click Me.";

		public Issue2681(TestDevice testDevice) : base(testDevice)
		{
		}

		public override string Issue => "[UWP] Label inside Listview gets stuck inside infinite loop";

		[Test]
		public void ListViewDoesntFreezeApp()
		{
			this.IgnoreIfPlatforms([TestDevice.Android, TestDevice.iOS, TestDevice.Mac]);

			App.Click(NavigateToPage);
			App.WaitForNoElement("3");
		}
	}
}