﻿// --------------------------------------------------------------------------------------------------------------------
// <summary>
//   Frame and scan info tests.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace UIMFLibrary.UnitTests.DataReaderTests
{
	using System;

	using NUnit.Framework;

	/// <summary>
	/// The frame and scan info tests.
	/// </summary>
	[TestFixture]
	public class FrameAndScanInfoTests
	{
		#region Public Methods and Operators

		/// <summary>
		/// The get avg tof length test 1.
		/// </summary>
		[Test]
		public void getAvgTOFLengthTest1()
		{
			using (DataReader reader = new DataReader(FileRefs.uimfStandardFile1))
			{
				double avgTOFLength = reader.GetFrameParameters(1).AverageTOFLength;

				Assert.AreEqual(162555.56m, (decimal)avgTOFLength);
			}
		}

		// TODO:  assert some values
		/// <summary>
		/// The get frame info_demultiplexed frame 0_ test 1.
		/// </summary>
		[Test]
		public void getFrameInfo_demultiplexedFrame0_Test1()
		{
			using (DataReader reader = new DataReader(FileRefs.uimfStandardDemultiplexedFile1))
			{
				int testFrame = 0;

				FrameParameters fp = reader.GetFrameParameters(testFrame);

				// TestUtilities.displayFrameParameters(fp);
			}
		}

		// TODO:  assert some values
		/// <summary>
		/// The get frame info_demultiplexed_first frame_ test 1.
		/// </summary>
		[Test]
		public void getFrameInfo_demultiplexed_firstFrame_Test1()
		{
			using (DataReader reader = new DataReader(FileRefs.uimfStandardDemultiplexedFile1))
			{
				int firstFrame = 0;

				FrameParameters fp = reader.GetFrameParameters(firstFrame);

				// TestUtilities.displayFrameParameters(fp);
			}
		}

		// TODO:  assert some values
		/// <summary>
		/// The get frame info_demultiplexed_last frame_ test 1.
		/// </summary>
		[Test]
		public void getFrameInfo_demultiplexed_lastFrame_Test1()
		{
			using (DataReader reader = new DataReader(FileRefs.uimfStandardDemultiplexedFile1))
			{
				int numFrames = reader.GetGlobalParameters().NumFrames;
				int lastFrame = numFrames - 1;

				Console.WriteLine("Last frame = " + lastFrame);

				FrameParameters fp = reader.GetFrameParameters(lastFrame);

				// TestUtilities.displayFrameParameters(fp);
			}
		}

		/// <summary>
		/// The get frame pressure_last frame.
		/// </summary>
		[Test]
		public void getFramePressure_lastFrame()
		{
			using (DataReader reader = new DataReader(FileRefs.uimfStandardFile1))
			{
				int lastFrame = 3219;
				int secondToLastFrame = lastFrame - 1;

				double pressureBackLastFrame = reader.GetFrameParameters(lastFrame).PressureBack;
				double pressureBackSecondToLastFrame = reader.GetFrameParameters(secondToLastFrame).PressureBack;

				// Console.WriteLine("Pressure for frame " + secondToLastFrame + " = " + pressureBackSecondToLastFrame);

				// Console.WriteLine("Pressure for frame "+lastFrame + " = " + pressureBackLastFrame);
				Assert.AreEqual(4.127, (decimal)pressureBackLastFrame);
			}
		}

		/// <summary>
		/// The get global params_test 1.
		/// </summary>
		[Test]
		public void getGlobalParams_test1()
		{
			using (DataReader reader = new DataReader(FileRefs.uimfStandardDemultiplexedFile1))
			{
				GlobalParameters gp = reader.GetGlobalParameters();
				DateTime dt = DateTime.Parse(gp.DateStarted);

				Assert.AreEqual("4/7/2011 6:40:30 AM", dt.ToString());
			}
		}

		/// <summary>
		/// The get number of frames test.
		/// </summary>
		[Test]
		public void getNumberOfFramesTest()
		{
			using (DataReader reader = new DataReader(FileRefs.uimfStandardFile1))
			{
				int numFrames = reader.GetGlobalParameters().NumFrames;

				// Console.WriteLine("Number of frames = " + numFrames);
				Assert.AreEqual(3220, numFrames);
			}
		}

		#endregion
	}
}