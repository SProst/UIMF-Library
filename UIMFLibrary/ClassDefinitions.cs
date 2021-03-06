﻿// --------------------------------------------------------------------------------------------------------------------
// <summary>
//   Global parameters.
// </summary>
// 
// --------------------------------------------------------------------------------------------------------------------
namespace UIMFLibrary
{
	using System;

	/// <summary>
	/// The global parameters.
	/// </summary>
	public class GlobalParameters
	{
		// public DateTime DateStarted;         // Date Experiment was acquired 

		#region Fields

		/// <summary>
		///Width of TOF bins (in ns)
		/// </summary>
		public double BinWidth;

		/// <summary>
		/// Total number of TOF bins in frame
		/// </summary>
		public int Bins;

		/// <summary>
		/// Type of dataset (HMS/HMSMS/HMS-MSn)
		/// </summary>
		public string DatasetType; 

		/// <summary>
		/// Date started.
		/// </summary>
		public string DateStarted;

		/// <summary>
		/// Version of FrameDataBlob in T_Frame
		/// </summary>
		public float FrameDataBlobVersion;

		/// <summary>
		/// Instrument name.
		/// </summary>
		public string InstrumentName;

		/// <summary>
		/// Number of frames in dataset
		/// </summary>
		public int NumFrames;

		/// <summary>
		/// Number of prescan accumulations
		/// </summary>
		public int Prescan_Accumulations;

		/// <summary>
		/// Prescan Continuous flag
		/// </summary>
		public bool Prescan_Continuous; 

		/// <summary>
		/// Prescan profile.
		/// </summary>
		/// <remarks>
		/// If continuous is true, set this to NULL;
		/// </remarks>
		public string Prescan_Profile;

		/// <summary>
		/// Prescan TIC threshold
		/// </summary>
		public int Prescan_TICThreshold;

		/// <summary>
		/// Prescan TOF pulses
		/// </summary>
		public int Prescan_TOFPulses;

		/// <summary>
		/// Version of ScanInfoBlob in T_Frame
		/// </summary>
		public float ScanDataBlobVersion;

		/// <summary>
		/// TOF correction time.
		/// </summary>
		public float TOFCorrectionTime;

		/// <summary>
		/// Data type of intensity in each TOF record (ADC is int, TDC is short, FOLDED is float)
		/// </summary>
		public string TOFIntensityType;

		/// <summary>
		/// Time offset from 0. All bin numbers must be offset by this amount
		/// </summary>
		public int TimeOffset;

		#endregion
	}

	/// <summary>
	/// The frame parameters.
	/// </summary>
	public class FrameParameters : ICloneable
	{
		#region Fields

		/// <summary>
		/// Number of collected and summed acquisitions in a frame 
		/// </summary>
		public int Accumulations; 

		/// <summary>
		/// Average tof length.
		/// </summary>
		/// <remarks>
		/// Average time between TOF trigger pulses
		/// </remarks>
		public double AverageTOFLength; // 8, 

		/// <summary>
		/// Tracks whether frame has been calibrated
		/// </summary>
		/// <remarks>
		/// Set to 1 after a frame has been calibrated
		/// </remarks>
		public int CalibrationDone = -1;

		/// <summary>
		/// Calibration intercept, t0
		/// </summary>
		public double CalibrationIntercept;

		/// <summary>
		/// Calibration slope, k0
		/// </summary>
		public double CalibrationSlope; 

		/// <summary>
		/// Tracks whether frame has been decoded
		/// </summary>
		/// <remarks>
		/// Set to 1 after a frame has been decoded (added June 27, 2011)
		/// </remarks>
		public int Decoded = 0;

		/// <summary>
		/// Frame duration, in seconds
		/// </summary>
		public double Duration;

		/// <summary>
		/// ESI voltage.
		/// </summary>
		public double ESIVoltage; 

		/// <summary>
		/// Float voltage.
		/// </summary>
		public double FloatVoltage;

		/// <summary>
		/// Voltage profile used in fragmentation
		/// </summary>
		public double[] FragmentationProfile;

		/// <summary>
		/// Frame number
		/// </summary>
		public int FrameNum; 

		/// <summary>
		/// Frame type
		/// </summary>
		/// <remarks>
		/// Bitmap: 0=MS (Legacy); 1=MS (Regular); 2=MS/MS (Frag); 3=Calibration; 4=Prescan
		/// </remarks>
		public DataReader.FrameType FrameType;

		/// <summary>
		/// High pressure funnel pressure.
		/// </summary>
		public double HighPressureFunnelPressure;

		/// <summary>
		/// IMFProfile Name
		/// </summary>
		/// <remarks>
		/// Stores the name of the sequence used to encode the data when acquiring data multiplexed
		/// </remarks>
		public string IMFProfile;		     

		/// <summary>
		/// Ion funnel trap pressure.
		/// </summary>
		public double IonFunnelTrapPressure;

		/// <summary>
		/// MP bit order
		/// </summary>
		/// <remarks>
		/// Determines original size of bit sequence 
		/// </remarks>
		public short MPBitOrder;

		/// <summary>
		/// Pressure at back of Drift Tube 
		/// </summary>
		public double PressureBack;

		/// <summary>
		///  Pressure at front of Drift Tube 
		/// </summary>
		public double PressureFront;

		/// <summary>
		/// Quadrupole pressure.
		/// </summary>
		public double QuadrupolePressure;

		/// <summary>
		/// Rear ion funnel pressure.
		/// </summary>
		public double RearIonFunnelPressure; 

		/// <summary>
		/// Number of TOF scans in a frame
		/// </summary>
		public int Scans;

		/// <summary>
		/// Start time of frame, in minutes
		/// </summary>
		public double StartTime;

		/// <summary>
		/// Number of TOF Losses (lost/skipped scans due to I/O problems)
		/// </summary>
		public double TOFLosses;

		/// <summary>
		/// Ambient temperature
		/// </summary>
		public double Temperature;

		/// <summary>
		/// a2 parameter for residual mass error correction
		/// </summary>
		/// <remarks>
		/// ResidualMassError = a2t + b2t^3 + c2t^5 + d2t^7 + e2t^9 + f2t^11
		/// </remarks>
		public double a2;

		/// <summary>
		/// b2 parameter for residual mass error correction
		/// </summary>
		public double b2;

		/// <summary>
		/// c2 parameter for residual mass error correction
		/// </summary>
		public double c2;

		/// <summary>
		/// d2 parameter for residual mass error correction
		/// </summary>
		public double d2;

		/// <summary>
		/// e2 parameter for residual mass error correction
		/// </summary>
		public double e2;

		/// <summary>
		/// f2 parameter for residual mass error correction
		/// </summary>
		/// <remarks>
		/// ResidualMassError = a2t + b2t^3 + c2t^5 + d2t^7 + e2t^9 + f2t^11
		/// </remarks>
		public double f2;

		/// <summary>
		/// Capillary Inlet Voltage
		/// </summary>
		public double voltCapInlet;

		/// <summary>
		/// Fragmentation Conductance Voltage
		/// </summary>
		public double voltCond1;

		/// <summary>
		/// Fragmentation Conductance Voltage
		/// </summary>
		public double voltCond2;

		/// <summary>
		/// Entrance Cond Limit Voltage
		/// </summary>
		public double voltEntranceCondLmt; 

		/// <summary>
		/// HPF In Voltage
		/// </summary>
		/// <remarks>
		/// Renamed from voltEntranceIFTIn to voltEntranceHPFIn in July 2011
		/// </remarks>
		public double voltEntranceHPFIn;


		/// <summary>
		/// HPF Out Voltage
		/// </summary>
		/// <remarks>
		/// Renamed from voltEntranceIFTOut to voltEntranceHPFOut in July 2011
		/// </remarks>
		public double voltEntranceHPFOut;

		/// <summary>
		/// Exit Cond Limit Voltage
		/// </summary>
		public double voltExitCondLmt; 

		/// <summary>
		/// HPF In Voltage
		/// </summary>
		/// /// <remarks>
		/// Renamed from voltExitIFTIn to voltExitHPFIn in July 2011
		/// </remarks>
		public double voltExitHPFIn;

		/// <summary>
		/// HPF Out Voltage
		/// </summary>
		/// /// <remarks>
		/// Renamed from voltExitIFTOut to voltExitHPFOut in July 2011
		/// </remarks>
		public double voltExitHPFOut;

		/// <summary>
		/// Volt hv rack 1.
		/// </summary>
		public double voltHVRack1; 

		/// <summary>
		/// Volt hv rack 2.
		/// </summary>
		public double voltHVRack2;

		/// <summary>
		/// Volt hv rack 3.
		/// </summary>
		public double voltHVRack3;

		/// <summary>
		/// Volt hv rack 4.
		/// </summary>
		public double voltHVRack4;

		/// <summary>
		/// IMS Out Voltage
		/// </summary>
		public double voltIMSOut;

		/// <summary>
		/// Jet Disruptor Voltage
		/// </summary>
		public double voltJetDist;

		/// <summary>
		/// Fragmentation Quadrupole Voltage 1
		/// </summary>
		public double voltQuad1;

		/// <summary>
		/// Fragmentation Quadrupole Voltage 2
		/// </summary>
		public double voltQuad2;

		/// <summary>
		/// Trap In Voltage
		/// </summary>
		public double voltTrapIn;

		/// <summary>
		/// Trap Out Voltage
		/// </summary>
		public double voltTrapOut;

		#endregion

		#region Constructors and Destructors

		/// <summary>
		/// Initializes a new instance of the <see cref="FrameParameters"/> class. 
		/// This constructor assumes the developer will manually store a value in StartTime
		/// </summary>
		public FrameParameters()
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="FrameParameters"/> class. 
		/// This constructor auto-populates StartTime using Now minutes dtRunStartTime using the correct format
		/// </summary>
		/// <param name="dtRunStartTime">
		/// </param>
		public FrameParameters(DateTime dtRunStartTime)
		{
			this.StartTime = System.DateTime.UtcNow.Subtract(dtRunStartTime).TotalMinutes;
		}

		#endregion

		#region Public Properties

		/// <summary>
		/// Included for backwards compatibility
		/// </summary>
		public double voltEntranceIFTIn
		{
			get
			{
				return this.voltEntranceHPFIn;
			}

			set
			{
				this.voltEntranceHPFIn = value;
			}
		}

		/// <summary>
		/// Included for backwards compatibility
		/// </summary>
		public double voltEntranceIFTOut
		{
			get
			{
				return this.voltEntranceHPFOut;
			}

			set
			{
				this.voltEntranceHPFOut = value;
			}
		}

		/// <summary>
		/// Included for backwards compatibility
		/// </summary>
		public double voltExitIFTIn
		{
			get
			{
				return this.voltExitHPFIn;
			}

			set
			{
				this.voltExitHPFIn = value;
			}
		}

		/// <summary>
		/// Included for backwards compatibility
		/// </summary>
		public double voltExitIFTOut
		{
			get
			{
				return this.voltExitHPFOut;
			}

			set
			{
				this.voltExitHPFOut = value;
			}
		}

		#endregion

        #region ICloneable Members

        /// <summary>
        /// Performs a deep copy of the entire object.
        /// </summary>
        /// <returns>New Frame Parameter</returns>
        public object Clone()
        {
            var target = new FrameParameters();

            target.FrameNum = this.FrameNum;
            target.StartTime = this.StartTime;
            target.Duration = this.Duration;
            target.Accumulations = this.Accumulations;
            target.FrameType = this.FrameType;
            target.Scans = this.Scans;
            target.IMFProfile = this.IMFProfile;
            target.TOFLosses = this.TOFLosses;
            target.AverageTOFLength = this.AverageTOFLength;
            target.CalibrationSlope = this.CalibrationSlope;
            target.CalibrationIntercept = this.CalibrationIntercept;
            target.a2 = this.a2;
            target.b2 = this.b2;
            target.c2 = this.c2;
            target.d2 = this.d2;
            target.e2 = this.e2;
            target.f2 = this.f2;
            target.Temperature = this.Temperature;
            target.voltHVRack1 = this.voltHVRack1;
            target.voltHVRack2 = this.voltHVRack2;
            target.voltHVRack3 = this.voltHVRack3;
            target.voltHVRack4 = this.voltHVRack4;
            target.voltCapInlet = this.voltCapInlet;
            target.voltEntranceHPFIn = this.voltEntranceHPFIn;
            target.voltEntranceHPFOut = this.voltEntranceHPFOut;
            target.voltEntranceCondLmt = this.voltEntranceCondLmt;
            target.voltTrapOut = this.voltTrapOut;
            target.voltTrapIn = this.voltTrapIn;
            target.voltJetDist = this.voltJetDist;
            target.voltQuad1 = this.voltQuad1;
            target.voltCond1 = this.voltCond1;
            target.voltQuad2 = this.voltQuad2;
            target.voltCond2 = this.voltCond2;
            target.voltIMSOut = this.voltIMSOut;
            target.voltExitHPFIn = this.voltExitHPFIn;
            target.voltExitHPFOut = this.voltExitHPFOut;
            target.voltExitCondLmt = this.voltExitCondLmt;
            target.PressureFront = this.PressureFront;
            target.PressureBack = this.PressureBack;
            target.MPBitOrder = this.MPBitOrder;

            if (this.FragmentationProfile != null)
            {
                target.FragmentationProfile = new double[this.FragmentationProfile.Length];
                Array.Copy(this.FragmentationProfile, target.FragmentationProfile, this.FragmentationProfile.Length);
            }

            target.HighPressureFunnelPressure = this.HighPressureFunnelPressure;
            target.IonFunnelTrapPressure = this.IonFunnelTrapPressure;
            target.RearIonFunnelPressure = this.RearIonFunnelPressure;
            target.QuadrupolePressure = this.QuadrupolePressure;
            target.ESIVoltage = this.ESIVoltage;
            target.FloatVoltage = this.FloatVoltage;
            target.CalibrationDone = this.CalibrationDone;
            target.Decoded = this.Decoded;

            return target;
        }

        #endregion
    }

	/// <summary>
	/// The m z_ calibrator.
	/// </summary>
	/// <remarks>
	/// Calibrate TOF to m/z according to formula: mass = (k * (t-t0))^2
	/// </remarks>
	public class MZ_Calibrator
	{
		#region Fields

		/// <summary>
		/// k
		/// </summary>
		private double K;

		/// <summary>
		/// t0
		/// </summary>
		private double T0;

		#endregion

		#region Constructors and Destructors

		/// <summary>
		/// Initializes a new instance of the <see cref="MZ_Calibrator"/> class.
		/// </summary>
		/// <param name="k">
		/// k
		/// </param>
		/// <param name="t0">
		/// t0
		/// </param>
		/// <remarks>
		/// mass = (k * (t-t0))^2
		/// </remarks>
		public MZ_Calibrator(double k, double t0)
		{
			this.K = k;
			this.T0 = t0;
		}

		#endregion

		#region Public Properties

		/// <summary>
		/// Gets the description.
		/// </summary>
		public string Description
		{
			get
			{
				return "mz = (k*(t-t0))^2";
			}
		}

		/// <summary>
		/// Gets or sets the k.
		/// </summary>
		public double k
		{
			get
			{
				return this.K;
			}

			set
			{
				this.K = value;
			}
		}

		/// <summary>
		/// Gets or sets the t 0.
		/// </summary>
		public double t0
		{
			get
			{
				return this.T0;
			}

			set
			{
				this.T0 = value;
			}
		}

		#endregion

		#region Public Methods and Operators

		/// <summary>
		/// Convert m/z to TOF value
		/// </summary>
		/// <param name="mz">
		/// mz
		/// </param>
		/// <returns>
		/// TOF value<see cref="int"/>.
		/// </returns>
		public int MZtoTOF(double mz)
		{
			double r = Math.Sqrt(mz);
			return (int)(((r / this.K) + this.T0) + .5); // .5 for rounding
		}

		/// <summary>
		/// Convert TOF value to m/z
		/// </summary>
		/// <param name="TOFValue">
		/// The tof value
		/// </param>
		/// <returns>
		/// m/z<see cref="double"/>.
		/// </returns>
		public double TOFtoMZ(double TOFValue)
		{
			double r = this.K * (TOFValue - this.T0);
			return r * r;
		}

		#endregion
	}
}