﻿/////////////////////////////////////////////////////////////////////////////////////
// This file includes a library of functions to retrieve data from a UIMF format file
// 
// Author:  Anuj Shaw
//          William Danielson
//          Yan Shi
//

using System;
using System.Data;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Data.SQLite;

namespace UIMFLibrary
{
    public class DataReader
    {
        private const int DATASIZE = 4; //All intensities are stored as 4 byte quantities
        private const int MAXMZ = 5000;
        private const int PARENTFRAMETYPE = 0;
        private const int CALIBRATIONFRAMETYPE = 3;
        private const int NUMCALIBRATIONFRAMES = 20;

        private const string BPI = "BPI";
        private const string TIC = "TIC";

        private Dictionary<int, FrameParameters> m_frameParametersCache;

        public SQLiteConnection m_uimfDatabaseConnection;
        // AARON: SQLiteDataReaders might be better managable than currently implement.
        public SQLiteDataReader m_sqliteDataReader;

        // v1.2 prepared statements
        public SQLiteCommand m_sumScansCommand;
        public SQLiteCommand m_getFileBytesCommand;
        public SQLiteCommand m_getFrameNumbers;
        public SQLiteCommand m_getSpectrumCommand;
        public SQLiteCommand m_getCountPerSpectrumCommand;
        public SQLiteCommand m_getCountPerFrameCommand;
        public SQLiteCommand m_sumScansCachedCommand;
        public SQLiteCommand m_getFrameParametersCommand;
        public SQLiteCommand dbcmd_PreparedStmt;
        public SQLiteCommand m_sumVariableScansPerFrameCommand;
        public SQLiteCommand m_getFramesAndScanByDescendingIntensityCommand;
        public SQLiteCommand m_getAllFrameParameters;


        private GlobalParameters m_globalParameters = null;
        public FrameParameters m_frameParameters = null;

        private int[] m_frameNumbers = null;
        private static int m_errMessageCounter = 0;

        public UIMFLibrary.MZ_Calibrator mz_Calibration;
        private double[] calibration_table = new double[0];
        private int filtered_FrameType = -1;
        private int[] array_FrameNum = null;
        private int current_FrameIndex = 0;

        public bool OpenUIMF(string FileName)
        {
            bool success = false;
            if (File.Exists(FileName))
            {
                string connectionString = "Data Source = " + FileName + "; Version=3; DateTimeFormat=Ticks;";
                m_uimfDatabaseConnection = new SQLiteConnection(connectionString);

                try
                {
                    m_uimfDatabaseConnection.Open();
                    //populate the global parameters object since it's going to be used anyways.
                    //I can't imagine a scenario where that wouldn't be the case.
                    m_globalParameters = GetGlobalParameters();
                    m_frameParametersCache = new Dictionary<int, FrameParameters>(m_globalParameters.NumFrames);
                    success = true;
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to open UIMF file " + ex.ToString());
                }
            }
            else
            {
                Console.WriteLine(FileName.ToString() + " does not exists");
            }

            if (success)
            {
                LoadPrepStmts();
                for (int i = 0; i < 4; i++)
                {
                    if (this.set_FrameType(i) > 0)
                        break; // all MS frames
                }
            }

            // Initialize caching structures
            return success;
        }

        private void LoadPrepStmts()
        {
            m_getFileBytesCommand = m_uimfDatabaseConnection.CreateCommand();

            // Table: Frame_Parameters
            m_getAllFrameParameters = m_uimfDatabaseConnection.CreateCommand();
            m_getAllFrameParameters.CommandText = "Select * from Frame_Parameters WHERE FrameType=:FrameType ORDER BY FrameNum";
            m_getAllFrameParameters.Prepare();

            m_getFrameNumbers = m_uimfDatabaseConnection.CreateCommand();
            m_getFrameNumbers.CommandText = "SELECT FrameNum FROM Frame_Parameters WHERE FrameType = :FrameType";
            m_getFrameNumbers.Prepare();

            m_getFrameParametersCommand = m_uimfDatabaseConnection.CreateCommand();
            m_getFrameParametersCommand.CommandText = "SELECT * FROM Frame_Parameters WHERE FrameNum = :FrameNum"; // FrameType not necessary
            m_getFrameParametersCommand.Prepare();

            // Table: Frame_Scans
            m_sumVariableScansPerFrameCommand = m_uimfDatabaseConnection.CreateCommand();
            m_getFramesAndScanByDescendingIntensityCommand = m_uimfDatabaseConnection.CreateCommand();
            m_getFramesAndScanByDescendingIntensityCommand.CommandText = "SELECT FrameNum, ScanNum, BPI FROM Frame_Scans ORDER BY BPI";
            m_getFramesAndScanByDescendingIntensityCommand.Prepare();

            m_sumScansCommand = m_uimfDatabaseConnection.CreateCommand();
            m_sumScansCommand.CommandText = "SELECT ScanNum, FrameNum,Intensities FROM Frame_Scans WHERE FrameNum >= :FrameNum1 AND FrameNum <= :FrameNum2 AND ScanNum >= :ScanNum1 AND ScanNum <= :ScanNum2";
            m_sumScansCommand.Prepare();

            m_sumScansCachedCommand = m_uimfDatabaseConnection.CreateCommand();
            m_sumScansCachedCommand.CommandText = "SELECT ScanNum, Intensities FROM Frame_Scans WHERE FrameNum = :FrameNumORDER BY ScanNum ASC";
            m_sumScansCachedCommand.Prepare();

            m_getSpectrumCommand = m_uimfDatabaseConnection.CreateCommand();
            m_getSpectrumCommand.CommandText = "SELECT Intensities FROM Frame_Scans WHERE FrameNum = :FrameNum AND ScanNum = :ScanNum";
            m_getSpectrumCommand.Prepare();

            m_getCountPerSpectrumCommand = m_uimfDatabaseConnection.CreateCommand();
            m_getCountPerSpectrumCommand.CommandText = "SELECT NonZeroCount FROM Frame_Scans WHERE FrameNum = :FrameNum AND ScanNum = :ScanNum";
            m_getCountPerSpectrumCommand.Prepare();

            m_getCountPerFrameCommand = m_uimfDatabaseConnection.CreateCommand();
            m_getCountPerFrameCommand.CommandText = "SELECT sum(NonZeroCount) FROM Frame_Scans WHERE FrameNum = :FrameNum";
            m_getCountPerFrameCommand.Prepare();
        }

        private void UnloadPrepStmts()
        {
            if (m_sumScansCommand != null)
            {
                m_sumScansCommand.Dispose();
            }

            if (m_getFrameNumbers != null)
            {
                m_getFrameNumbers.Dispose();
            }

            if (m_getCountPerSpectrumCommand != null)
            {
                m_getCountPerSpectrumCommand.Dispose();
            }

            if (m_getCountPerFrameCommand != null)
            {
                m_getCountPerFrameCommand.Dispose();
            }

            if (m_getFrameParametersCommand != null)
            {
                m_getFrameParametersCommand.Dispose();
            }

            if (m_sumScansCachedCommand != null)
            {
                m_sumScansCachedCommand.Dispose();
            }

        }

        /**
         * Overloaded method to close the connection to the UIMF file.
         * Unsure if closing the UIMF file requires a filename.
         * 
         * */

        public bool CloseUIMF()
        {
            UnloadPrepStmts();

            bool success = false;
            try
            {
                if (m_uimfDatabaseConnection != null)
                {
                    success = true;
                    m_uimfDatabaseConnection.Close();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to close UIMF file " + ex.ToString());
            }

            return success;

        }

        public bool CloseUIMF(string FileName)
        {
            return CloseUIMF();
        }

        //We want to make sure that this method is only called once. On first call, 
        //we have to populate the global parameters object/
        //Also this should return a strongly typed object as opposed to a generic one

        public GlobalParameters GetGlobalParameters()
        {
            //this variable will disappear in a bit
            //bool success = true;
            if (m_globalParameters == null)
            {
                //Retrieve it from the database
                if (m_uimfDatabaseConnection == null)
                {
                    //this means that yo'uve called this method without opening the UIMF file.
                    //should throw an exception saying UIMF file not opened here
                    //for now, let's just set an error flag
                    //success = false;
                    //the custom exception class has to be defined as yet
                }
                else
                {
                    m_globalParameters = new GlobalParameters();

                    //ARS: Don't know why this is a member variable, should be a local variable
                    //also they need to be named appropriately and don't need any UIMF extension to it
                    SQLiteCommand dbCmd = m_uimfDatabaseConnection.CreateCommand();
                    dbCmd.CommandText = "SELECT * FROM Global_Parameters";

                    //ARS: Don't know why this is a member variable, should be a local variable 
                    SQLiteDataReader reader = dbCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        try
                        {

                            m_globalParameters.BinWidth = Convert.ToDouble(reader["BinWidth"]);
                            m_globalParameters.DateStarted = Convert.ToString(reader["DateStarted"]);
                            m_globalParameters.NumFrames = Convert.ToInt32(reader["NumFrames"]);
                            m_globalParameters.TimeOffset = Convert.ToInt32(reader["TimeOffset"]);
                            m_globalParameters.BinWidth = Convert.ToDouble(reader["BinWidth"]);
                            m_globalParameters.Bins = Convert.ToInt32(reader["Bins"]);
                            try
                            {
                                m_globalParameters.TOFCorrectionTime = Convert.ToSingle(reader["TOFCorrectionTime"]);
                            }
                            catch
                            {
                                m_errMessageCounter++;
                                Console.WriteLine("Warning: this UIMF file is created with an old version of IMF2UIMF, please get the newest version from \\\\floyd\\software");
                            }
                            m_globalParameters.Prescan_TOFPulses = Convert.ToInt32(reader["Prescan_TOFPulses"]);
                            m_globalParameters.Prescan_Accumulations = Convert.ToInt32(reader["Prescan_Accumulations"]);
                            m_globalParameters.Prescan_TICThreshold = Convert.ToInt32(reader["Prescan_TICThreshold"]);
                            m_globalParameters.Prescan_Continuous = Convert.ToBoolean(reader["Prescan_Continuous"]);
                            m_globalParameters.Prescan_Profile = Convert.ToString(reader["Prescan_Profile"]);
                            m_globalParameters.FrameDataBlobVersion = (float)Convert.ToDouble((reader["FrameDataBlobVersion"]));
                            m_globalParameters.ScanDataBlobVersion = (float)Convert.ToDouble((reader["ScanDataBlobVersion"]));
                            m_globalParameters.TOFIntensityType = Convert.ToString(reader["TOFIntensityType"]);
                            m_globalParameters.DatasetType = Convert.ToString(reader["DatasetType"]);
                            try
                            {
                                m_globalParameters.InstrumentName = Convert.ToString(reader["Instrument_Name"]);
                            }
                            catch (Exception e)
                            {
                                //ignore since it may not be present in all previous versions
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("Failed to get global parameters " + ex.ToString());
                        }
                    }

                    dbCmd.Dispose();
                    reader.Close();
                }
            }

            return m_globalParameters;
        }

        /// <summary>
        /// Utility method to return the MS Level for a particular frame
        /// 
        /// 
        /// </summary>
        /// <param name="frameNumber"></param>
        /// <returns></returns>
        public short GetMSLevelForFrame(int frame_index)
        {
            return GetFrameParameters(this.array_FrameNum[frame_index]).FrameType;
        }

        public FrameParameters GetFrameParameters(int frame_index)
        {
            if (frame_index < 0)
            {
                throw new Exception("FrameNum should be a positive integer");
            }

            FrameParameters fp = new FrameParameters();

            this.current_FrameIndex = frame_index;

            //now check in cache first
            if (m_frameParametersCache.ContainsKey(this.array_FrameNum[frame_index]))
            {
                //frame parameters object is cached, retrieve it and return
                //fp = (FrameParameters) mFrameParametersCache[frameNumber];
                fp = m_frameParametersCache[this.array_FrameNum[frame_index]];
            }
            else
            {
                //else we have to retrieve it and store it in the cache for future reference
                if (m_uimfDatabaseConnection != null)
                {
                    m_getFrameParametersCommand.Parameters.Add(new SQLiteParameter("FrameNum", this.array_FrameNum[frame_index]));

                    SQLiteDataReader reader = m_getFrameParametersCommand.ExecuteReader();
                    while (reader.Read())
                    {
                        try
                        {
                            int columnMissingCounter = 0;

                            fp.FrameNum = Convert.ToInt32(reader["FrameNum"]);
                            fp.StartTime = Convert.ToDouble(reader["StartTime"]);
                            fp.Duration = Convert.ToDouble(reader["Duration"]);
                            fp.Accumulations = Convert.ToInt32(reader["Accumulations"]);
                            fp.FrameType = (short)Convert.ToInt16(reader["FrameType"]);
                            fp.Scans = Convert.ToInt32(reader["Scans"]);
                            fp.IMFProfile = Convert.ToString(reader["IMFProfile"]);
                            fp.TOFLosses = Convert.ToDouble(reader["TOFLosses"]);
                            fp.AverageTOFLength = Convert.ToDouble(reader["AverageTOFLength"]);
                            fp.CalibrationSlope = Convert.ToDouble(reader["CalibrationSlope"]);
                            fp.CalibrationIntercept = Convert.ToDouble(reader["CalibrationIntercept"]);
                            fp.Temperature = Convert.ToDouble(reader["Temperature"]);
                            fp.voltHVRack1 = Convert.ToDouble(reader["voltHVRack1"]);
                            fp.voltHVRack2 = Convert.ToDouble(reader["voltHVRack2"]);
                            fp.voltHVRack3 = Convert.ToDouble(reader["voltHVRack3"]);
                            fp.voltHVRack4 = Convert.ToDouble(reader["voltHVRack4"]);
                            fp.voltCapInlet = Convert.ToDouble(reader["voltCapInlet"]); // 14, Capilary Inlet Voltage
                            fp.voltEntranceIFTIn = Convert.ToDouble(reader["voltEntranceIFTIn"]); // 15, IFT In Voltage
                            fp.voltEntranceIFTOut = Convert.ToDouble(reader["voltEntranceIFTOut"]); // 16, IFT Out Voltage
                            fp.voltEntranceCondLmt = Convert.ToDouble(reader["voltEntranceCondLmt"]); // 17, Cond Limit Voltage
                            fp.voltTrapOut = Convert.ToDouble(reader["voltTrapOut"]); // 18, Trap Out Voltage
                            fp.voltTrapIn = Convert.ToDouble(reader["voltTrapIn"]); // 19, Trap In Voltage
                            fp.voltJetDist = Convert.ToDouble(reader["voltJetDist"]);              // 20, Jet Disruptor Voltage
                            fp.voltQuad1 = Convert.ToDouble(reader["voltQuad1"]);                // 21, Fragmentation Quadrupole Voltage
                            fp.voltCond1 = Convert.ToDouble(reader["voltCond1"]);                // 22, Fragmentation Conductance Voltage
                            fp.voltQuad2 = Convert.ToDouble(reader["voltQuad2"]);                // 23, Fragmentation Quadrupole Voltage
                            fp.voltCond2 = Convert.ToDouble(reader["voltCond2"]);                // 24, Fragmentation Conductance Voltage
                            fp.voltIMSOut = Convert.ToDouble(reader["voltIMSOut"]);               // 25, IMS Out Voltage
                            fp.voltExitIFTIn = Convert.ToDouble(reader["voltExitIFTIn"]);            // 26, IFT In Voltage
                            fp.voltExitIFTOut = Convert.ToDouble(reader["voltExitIFTOut"]);           // 27, IFT Out Voltage
                            fp.voltExitCondLmt = Convert.ToDouble(reader["voltExitCondLmt"]);           // 28, Cond Limit Voltage
                            fp.PressureFront = Convert.ToDouble(reader["PressureFront"]);
                            fp.PressureBack = Convert.ToDouble(reader["PressureBack"]);
                            fp.MPBitOrder = (short)Convert.ToInt32(reader["MPBitOrder"]);
                            fp.FragmentationProfile = array_FragmentationSequence((byte[])(reader["FragmentationProfile"]));

                            fp.HighPressureFunnelPressure = TryGetFrameParam(reader, "HighPressureFunnelPressure", 0, ref columnMissingCounter);
                            if (columnMissingCounter > 0)
                            {
                                if (m_errMessageCounter < 5)
                                {
                                    Console.WriteLine("Warning: this UIMF file is created with an old version of IMF2UIMF (missing one or more expected columns); please get the newest version from \\\\floyd\\software");
                                    m_errMessageCounter++;
                                }
                            }
                            else
                            {
                                fp.IonFunnelTrapPressure = TryGetFrameParam(reader, "IonFunnelTrapPressure", 0, ref columnMissingCounter);
                                fp.RearIonFunnelPressure = TryGetFrameParam(reader, "RearIonFunnelPressure", 0, ref columnMissingCounter);
                                fp.QuadrupolePressure = TryGetFrameParam(reader, "QuadrupolePressure", 0, ref columnMissingCounter);
                                fp.ESIVoltage = TryGetFrameParam(reader, "ESIVoltage", 0, ref columnMissingCounter);
                                fp.FloatVoltage = TryGetFrameParam(reader, "FloatVoltage", 0, ref columnMissingCounter);
                                fp.CalibrationDone = TryGetFrameParamInt32(reader, "CALIBRATIONDONE", 0, ref columnMissingCounter);
                            }


                            fp.a2 = TryGetFrameParam(reader, "a2", 0, ref columnMissingCounter);
                            if (columnMissingCounter > 0)
                            {
                                fp.b2 = 0;
                                fp.c2 = 0;
                                fp.d2 = 0;
                                fp.e2 = 0;
                                fp.f2 = 0;
                                if (m_errMessageCounter < 5)
                                {
                                    Console.WriteLine("Warning: this UIMF file is created with an old version of IMF2UIMF (missing one or more expected columns); please get the newest version from \\\\floyd\\software");
                                    m_errMessageCounter++;
                                }
                            }
                            else
                            {
                                fp.b2 = TryGetFrameParam(reader, "b2", 0, ref columnMissingCounter);
                                fp.c2 = TryGetFrameParam(reader, "c2", 0, ref columnMissingCounter);
                                fp.d2 = TryGetFrameParam(reader, "d2", 0, ref columnMissingCounter);
                                fp.e2 = TryGetFrameParam(reader, "e2", 0, ref columnMissingCounter);
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("Failed to access frame parameters table " + ex.ToString());
                        }
                    }//end of while loop

                    //store it in the cache for future
                    m_frameParametersCache.Add(this.array_FrameNum[frame_index], fp);
                    m_getFrameParametersCommand.Parameters.Clear();
                    reader.Close();
                }//end of if loop
            }

            this.mz_Calibration = new UIMFLibrary.MZ_Calibrator(fp.CalibrationSlope / 10000.0,
                fp.CalibrationIntercept * 10000.0);

            return fp;
        }


        public List<string> getCalibrationTableNames()
        {
            SQLiteDataReader reader = null;
            SQLiteCommand cmd = new SQLiteCommand(m_uimfDatabaseConnection);
            cmd.CommandText = "SELECT NAME FROM Sqlite_master WHERE type='table' ORDER BY NAME";
            List<string> calibrationTableNames = new List<string>(NUMCALIBRATIONFRAMES);
            try
            {

                reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string tableName = Convert.ToString(reader["Name"]);
                    if (tableName.Contains("Calib"))
                    {
                        calibrationTableNames.Add(tableName);
                    }
                }
            }
            catch (Exception a)
            {
            }

            return calibrationTableNames;

        }

        protected double TryGetFrameParam(SQLiteDataReader reader, string ColumnName, double DefaultValue, ref int columnMissingCounter)
        {
            double Result = DefaultValue;

            try
            {
                Result = Convert.ToDouble(reader[ColumnName]);
            }
            catch (IndexOutOfRangeException i)
            {
                columnMissingCounter += 1;
            }

            return Result;
        }

        protected int TryGetFrameParamInt32(SQLiteDataReader reader, string ColumnName, int DefaultValue, ref int columnMissingCounter)
        {
            int Result = DefaultValue;

            try
            {
                Result = Convert.ToInt32(reader[ColumnName]);
            }
            catch (IndexOutOfRangeException i)
            {
                columnMissingCounter += 1;
            }

            return Result;
        }

        public bool tableExists(string tableName)
        {
            SQLiteCommand cmd = new SQLiteCommand(m_uimfDatabaseConnection);
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE name='" + tableName + "'";
            SQLiteDataReader rdr = cmd.ExecuteReader();
            if (rdr.HasRows)
                return true;
            else
                return false;
        }


        /**
         * Method to provide the bytes from tables that store metadata files 
         */
        public byte[] getFileBytesFromTable(string tableName)
        {
            SQLiteDataReader reader = null;
            byte[] byteBuffer = null;

            try
            {
                m_getFileBytesCommand.CommandText = "SELECT FileText from " + tableName;

                if (tableExists(tableName))
                {
                    reader = m_getFileBytesCommand.ExecuteReader();
                    while (reader.Read())
                    {

                        byteBuffer = (byte[])(reader["FileText"]);
                    }
                }
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
            return byteBuffer;
        }

        public Dictionary<int, FrameParameters> GetAllParentFrameParameters()
        {
            return GetAllFrameParameters(PARENTFRAMETYPE);
        }

        public Dictionary<int, FrameParameters> GetAllCalibrationFrameParameters()
        {
            return GetAllFrameParameters(CALIBRATIONFRAMETYPE);
        }


        /**
         * Returns the list of all frame parameters in order of sorted frame numbers
         * 
         */
        public Dictionary<int, FrameParameters> GetAllFrameParameters(int frameType)
        {
            SQLiteDataReader reader = null;

            try
            {
                m_getAllFrameParameters.Parameters.Add(new SQLiteParameter(":FrameType", frameType));

                reader = m_getAllFrameParameters.ExecuteReader();
                while (reader.Read())
                {
                    FrameParameters fp = new FrameParameters();
                    int columnMissingCounter = 0;

                    fp.FrameNum = Convert.ToInt32(reader["FrameNum"]);
                    if (!m_frameParametersCache.ContainsKey(fp.FrameNum))
                    {
                        fp.Duration = Convert.ToDouble(reader["Duration"]);
                        fp.Accumulations = Convert.ToInt32(reader["Accumulations"]);
                        fp.FrameType = (short)Convert.ToInt16(reader["FrameType"]);
                        fp.Scans = Convert.ToInt32(reader["Scans"]);
                        fp.IMFProfile = Convert.ToString(reader["IMFProfile"]);
                        fp.TOFLosses = Convert.ToDouble(reader["TOFLosses"]);
                        fp.AverageTOFLength = Convert.ToDouble(reader["AverageTOFLength"]);
                        fp.CalibrationSlope = Convert.ToDouble(reader["CalibrationSlope"]);
                        fp.CalibrationIntercept = Convert.ToDouble(reader["CalibrationIntercept"]);
                        fp.Temperature = Convert.ToDouble(reader["Temperature"]);
                        fp.voltHVRack1 = Convert.ToDouble(reader["voltHVRack1"]);
                        fp.voltHVRack2 = Convert.ToDouble(reader["voltHVRack2"]);
                        fp.voltHVRack3 = Convert.ToDouble(reader["voltHVRack3"]);
                        fp.voltHVRack4 = Convert.ToDouble(reader["voltHVRack4"]);
                        fp.voltCapInlet = Convert.ToDouble(reader["voltCapInlet"]); // 14, Capilary Inlet Voltage
                        fp.voltEntranceIFTIn = Convert.ToDouble(reader["voltEntranceIFTIn"]); // 15, IFT In Voltage
                        fp.voltEntranceIFTOut = Convert.ToDouble(reader["voltEntranceIFTOut"]); // 16, IFT Out Voltage
                        fp.voltEntranceCondLmt = Convert.ToDouble(reader["voltEntranceCondLmt"]); // 17, Cond Limit Voltage
                        fp.voltTrapOut = Convert.ToDouble(reader["voltTrapOut"]); // 18, Trap Out Voltage
                        fp.voltTrapIn = Convert.ToDouble(reader["voltTrapIn"]); // 19, Trap In Voltage
                        fp.voltJetDist = Convert.ToDouble(reader["voltJetDist"]);              // 20, Jet Disruptor Voltage
                        fp.voltQuad1 = Convert.ToDouble(reader["voltQuad1"]);                // 21, Fragmentation Quadrupole Voltage
                        fp.voltCond1 = Convert.ToDouble(reader["voltCond1"]);                // 22, Fragmentation Conductance Voltage
                        fp.voltQuad2 = Convert.ToDouble(reader["voltQuad2"]);                // 23, Fragmentation Quadrupole Voltage
                        fp.voltCond2 = Convert.ToDouble(reader["voltCond2"]);                // 24, Fragmentation Conductance Voltage
                        fp.voltIMSOut = Convert.ToDouble(reader["voltIMSOut"]);               // 25, IMS Out Voltage
                        fp.voltExitIFTIn = Convert.ToDouble(reader["voltExitIFTIn"]);            // 26, IFT In Voltage
                        fp.voltExitIFTOut = Convert.ToDouble(reader["voltExitIFTOut"]);           // 27, IFT Out Voltage
                        fp.voltExitCondLmt = Convert.ToDouble(reader["voltExitCondLmt"]);           // 28, Cond Limit Voltage
                        fp.PressureFront = Convert.ToDouble(reader["PressureFront"]);
                        fp.PressureBack = Convert.ToDouble(reader["PressureBack"]);
                        fp.MPBitOrder = (short)Convert.ToInt32(reader["MPBitOrder"]);
                        fp.FragmentationProfile = array_FragmentationSequence((byte[])(reader["FragmentationProfile"]));
                        fp.HighPressureFunnelPressure = TryGetFrameParam(reader, "HighPressureFunnelPressure", 0, ref columnMissingCounter);
                        fp.IonFunnelTrapPressure = TryGetFrameParam(reader, "IonFunnelTrapPressure", 0, ref columnMissingCounter);
                        fp.RearIonFunnelPressure = TryGetFrameParam(reader, "RearIonFunnelPressure", 0, ref columnMissingCounter);
                        fp.QuadrupolePressure = TryGetFrameParam(reader, "QuadrupolePressure", 0, ref columnMissingCounter);
                        fp.ESIVoltage = TryGetFrameParam(reader, "ESIVoltage", 0, ref columnMissingCounter);
                        fp.FloatVoltage = TryGetFrameParam(reader, "FloatVoltage", 0, ref columnMissingCounter);
                        fp.CalibrationDone = TryGetFrameParamInt32(reader, "CALIBRATIONDONE", 0, ref columnMissingCounter);
                        fp.a2 = TryGetFrameParam(reader, "a2", 0, ref columnMissingCounter);
                        fp.b2 = TryGetFrameParam(reader, "b2", 0, ref columnMissingCounter);
                        fp.c2 = TryGetFrameParam(reader, "c2", 0, ref columnMissingCounter);
                        fp.d2 = TryGetFrameParam(reader, "d2", 0, ref columnMissingCounter);
                        fp.e2 = TryGetFrameParam(reader, "e2", 0, ref columnMissingCounter);

                        m_frameParametersCache.Add(fp.FrameNum, fp);
                    }
                }
            }      // end of if loop
            finally
            {
                m_getAllFrameParameters.Parameters.Clear();
                reader.Close();
            }

            return m_frameParametersCache;
        }

        public void GetSpectrum(int frame_index, int scanNum, List<int> bins, List<int> intensities)
        {
            if (frame_index < 0 || scanNum < 0)
            {
                throw new Exception("Check if frame number or scan number is a positive integer");
            }

            //Testing a prepared statement
            m_getSpectrumCommand.Parameters.Add(new SQLiteParameter(":FrameNum", this.array_FrameNum[frame_index]));
            m_getSpectrumCommand.Parameters.Add(new SQLiteParameter(":ScanNum", scanNum));

            SQLiteDataReader reader = m_getSpectrumCommand.ExecuteReader();

            int nNonZero = 0;

            byte[] SpectraRecord;

            //Get the number of points that are non-zero in this frame and scan
            int expectedCount = GetCountPerSpectrum(this.array_FrameNum[frame_index], scanNum);

            if (expectedCount > 0)
            {
                //this should not be longer than expected count, 
                byte[] decomp_SpectraRecord = new byte[expectedCount * DATASIZE * 5];

                int ibin = 0;
                while (reader.Read())
                {
                    int out_len;
                    SpectraRecord = (byte[])(reader["Intensities"]);
                    if (SpectraRecord.Length > 0)
                    {
                        out_len = IMSCOMP_wrapper.decompress_lzf(ref SpectraRecord, SpectraRecord.Length, ref decomp_SpectraRecord, m_globalParameters.Bins * DATASIZE);

                        int numBins = out_len / DATASIZE;
                        int decoded_SpectraRecord;
                        for (int i = 0; i < numBins; i++)
                        {
                            decoded_SpectraRecord = BitConverter.ToInt32(decomp_SpectraRecord, i * DATASIZE);
                            if (decoded_SpectraRecord < 0)
                            {
                                ibin += -decoded_SpectraRecord;
                            }
                            else
                            {
                                bins.Add(ibin);
                                intensities.Add(decoded_SpectraRecord);
                                ibin++;
                                nNonZero++;
                            }

                        }
                    }
                }
            }
            m_getSpectrumCommand.Parameters.Clear();
            reader.Close();
        }

        public int GetSpectrum(int frame_index, int scanNum, int[] spectrum, int[] bins)
        {
            if (frame_index < 0 || scanNum < 0)
            {
                throw new Exception("Check if frame number or scan number is a positive integer.\n FrameNum starts at 0, ScanNum starts at 0");
            }

            //Testing a prepared statement
            m_getSpectrumCommand.Parameters.Add(new SQLiteParameter(":FrameNum", this.array_FrameNum[frame_index]));
            m_getSpectrumCommand.Parameters.Add(new SQLiteParameter(":ScanNum", scanNum));

            SQLiteDataReader reader = m_getSpectrumCommand.ExecuteReader();

            int nNonZero = 0;

            byte[] SpectraRecord;

            //Get the number of points that are non-zero in this frame and scan
            int expectedCount = GetCountPerSpectrum(this.array_FrameNum[frame_index], scanNum);

            //this should not be longer than expected count, however the IMSCOMP 
            //compression library requires a longer buffer since it does an inplace
            //decompression of the integer values and then reports only the length
            //of the points that have meaningful value.
            byte[] decomp_SpectraRecord = new byte[expectedCount * DATASIZE * 5];

            int ibin = 0;
            while (reader.Read())
            {
                int out_len;
                SpectraRecord = (byte[])(reader["Intensities"]);
                if (SpectraRecord.Length > 0)
                {
                    out_len = IMSCOMP_wrapper.decompress_lzf(ref SpectraRecord, SpectraRecord.Length, ref decomp_SpectraRecord, m_globalParameters.Bins * DATASIZE);

                    int numBins = out_len / DATASIZE;
                    int decoded_SpectraRecord;
                    for (int i = 0; i < numBins; i++)
                    {
                        decoded_SpectraRecord = BitConverter.ToInt32(decomp_SpectraRecord, i * DATASIZE);
                        if (decoded_SpectraRecord < 0)
                        {
                            ibin += -decoded_SpectraRecord;
                        }
                        else
                        {
                            bins[nNonZero] = ibin;
                            spectrum[nNonZero] = decoded_SpectraRecord;
                            ibin++;
                            nNonZero++;
                        }
                    }
                }
            }

            m_getSpectrumCommand.Parameters.Clear();
            reader.Close();

            return nNonZero;
        }

        public int GetCountPerFrame(int frame_index)
        {
            int countPerFrame = 0;
            m_getCountPerFrameCommand.Parameters.Add(new SQLiteParameter(":FrameNum", this.array_FrameNum[frame_index]));

            try
            {
                SQLiteDataReader reader = m_getCountPerFrameCommand.ExecuteReader();
                while (reader.Read())
                {
                    countPerFrame = Convert.ToInt32(reader[0]);
                }
                m_getCountPerFrameCommand.Parameters.Clear();
                reader.Dispose();
                reader.Close();
            }
            catch
            {
                countPerFrame = 1;
            }

            return countPerFrame;
        }

        // returns the frame numbers associated with the current frame_type
        public int[] GetFrameNumbers()
        {
            return this.array_FrameNum;
        }

        public int[] GetFrameNumbers(int frame_type)
        {
            this.set_FrameType(frame_type);

            return this.array_FrameNum;
        }


        public int GetCountPerSpectrum(int frame_index, int scan_num)
        {
            int countPerSpectrum = 0;
            m_getCountPerSpectrumCommand.Parameters.Add(new SQLiteParameter(":FrameNum", this.array_FrameNum[frame_index]));
            m_getCountPerSpectrumCommand.Parameters.Add(new SQLiteParameter(":ScanNum", scan_num));

            try
            {

                SQLiteDataReader reader = m_getCountPerSpectrumCommand.ExecuteReader();
                while (reader.Read())
                {
                    countPerSpectrum = Convert.ToInt32(reader[0]);
                }
                m_getCountPerSpectrumCommand.Parameters.Clear();
                reader.Dispose();
                reader.Close();
            }
            catch
            {
                countPerSpectrum = 1;
            }

            return countPerSpectrum;
        }


        public int[][][] GetIntensityBlock(int startFrameIndex, int endFrameIndex, int frameType, int startScan, int endScan, int startBin, int endBin)
        {
            bool proceed = false;
            int[][][] intensities = null;


            if (startBin < 0)
            {
                startBin = 0;
            }

            if (endBin > m_globalParameters.Bins)
            {
                endBin = m_globalParameters.Bins;
            }



            bool inputFrameRangesAreOK = (startFrameIndex >= 0) && (endFrameIndex >= startFrameIndex);
            if (!inputFrameRangesAreOK)
            {
                throw new ArgumentOutOfRangeException("Error getting intensities. Check the start and stop Frames values.");
            }
           
            bool inputScanRangesAreOK = (startScan >= 0 && endScan >= startScan);
            if (!inputScanRangesAreOK)
            {
                throw new ArgumentOutOfRangeException("Error getting intensities. Check the start and stop IMS Scan values.");
  
            }


            bool inputBinsAreOK = (endBin >= startBin);
            if (!inputBinsAreOK)
            {
                throw new ArgumentOutOfRangeException("Error getting intensities. Check the start and stop bin values.");
            }


            bool inputsAreOK = (inputFrameRangesAreOK && inputScanRangesAreOK && inputBinsAreOK);

            //initialize the intensities return two-D array

            int lengthOfFrameArray = (endFrameIndex - startFrameIndex + 1);

            intensities = new int[lengthOfFrameArray][][];
            for (int i = 0; i < lengthOfFrameArray; i++)
            {
                intensities[i] = new int[endScan - startScan + 1][];
                for (int j = 0; j < endScan - startScan + 1; j++)
                {
                    intensities[i][j] = new int[endBin - startBin + 1];
                }
            }

            //now setup queries to retrieve data (AARON: there is probably a better query method for this)
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("FrameNum1", this.array_FrameNum[startFrameIndex]));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("FrameNum2", this.array_FrameNum[endFrameIndex]));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("ScanNum1", startScan));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("ScanNum2", endScan));
            SQLiteDataReader reader = m_sumScansCommand.ExecuteReader();

            byte[] spectra;
            byte[] decomp_SpectraRecord = new byte[m_globalParameters.Bins * DATASIZE];

            while (reader.Read())
            {
                int frameNum = Convert.ToInt32(reader["FrameNum"]);
                int frame_index = this.get_FrameIndex(frameNum);

                if (frame_index < 0) // frame not correct type
                    continue;

                int ibin = 0;
                int out_len;

                spectra = (byte[])(reader["Intensities"]);
                int scanNum = Convert.ToInt32(reader["ScanNum"]);

                //get frame number so that we can get the frame calibration parameters
                if (spectra.Length > 0)
                {
                    out_len = IMSCOMP_wrapper.decompress_lzf(ref spectra, spectra.Length, ref decomp_SpectraRecord, m_globalParameters.Bins * DATASIZE);
                    int numBins = out_len / DATASIZE;
                    int decoded_intensityValue;
                    for (int i = 0; i < numBins; i++)
                    {
                        decoded_intensityValue = BitConverter.ToInt32(decomp_SpectraRecord, i * DATASIZE);
                        if (decoded_intensityValue < 0)
                        {
                            ibin += -decoded_intensityValue;
                        }
                        else
                        {
                            if (startBin <= ibin && ibin <= endBin)
                            {
                                intensities[frame_index - startFrameIndex][scanNum - startScan][ibin - startBin] = decoded_intensityValue;
                            }
                            ibin++;
                        }
                    }
                }
            }
            reader.Close();



            return intensities;
        }


        /**
         * @description:
         *         //this method returns all the intensities without summing for that block
                   //The return value is a 2-D array that returns all the intensities within the given scan range and bin range
                   //The number of rows is equal to endScan-startScan+1 and the number of columns is equal to endBin-startBin+1 
                   //If frame is added to this equation then we'll have to return a 3-D array of data values.

                   //The startScan is stored at the zeroth location and so is the startBin. Callers of this method should offset
                   //the retrieved values.

         * */
        public int[][] GetIntensityBlock(int frame_index, int frameType, int startScan, int endScan, int startBin, int endBin)
        {
            int[][] intensities = null;
            FrameParameters fp = null;

            if ((frame_index < 0) || (frame_index >= this.get_NumFrames(frameType)))
            {
                throw new Exception("ERROR GetIntensityBlock(): (0 > frame_index=" + frame_index.ToString() + " >= " + this.get_NumFrames(frameType).ToString() + ")");
            }

            fp = GetFrameParameters(frame_index);

            //check input parameters
            if (fp != null && (endScan - startScan) >= 0 && (endBin - startBin) >= 0 && fp.Scans > 0)
            {

                if (endBin > m_globalParameters.Bins)
                {
                    endBin = m_globalParameters.Bins;
                }

                if (startBin < 0)
                {
                    startBin = 0;
                }
            }

            //initialize the intensities return two-D array
            intensities = new int[endScan - startScan + 1][];
            for (int i = 0; i < endScan - startScan + 1; i++)
            {
                intensities[i] = new int[endBin - startBin + 1];
            }

            //now setup queries to retrieve data
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("FrameNum1", fp.FrameNum));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("FrameNum2", fp.FrameNum));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("ScanNum1", startScan));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("ScanNum2", endScan));
            m_sqliteDataReader = m_sumScansCommand.ExecuteReader();

            byte[] spectra;
            byte[] decomp_SpectraRecord = new byte[m_globalParameters.Bins * DATASIZE];

            while (m_sqliteDataReader.Read())
            {
                int ibin = 0;
                int out_len;

                spectra = (byte[])(m_sqliteDataReader["Intensities"]);
                int scanNum = Convert.ToInt32(m_sqliteDataReader["ScanNum"]);

                //get frame number so that we can get the frame calibration parameters
                if (spectra.Length > 0)
                {
                    out_len = IMSCOMP_wrapper.decompress_lzf(ref spectra, spectra.Length, ref decomp_SpectraRecord, m_globalParameters.Bins * DATASIZE);
                    int numBins = out_len / DATASIZE;
                    int decoded_intensityValue;
                    for (int i = 0; i < numBins; i++)
                    {
                        decoded_intensityValue = BitConverter.ToInt32(decomp_SpectraRecord, i * DATASIZE);
                        if (decoded_intensityValue < 0)
                        {
                            ibin += -decoded_intensityValue;
                        }
                        else
                        {
                            if (startBin <= ibin && ibin <= endBin)
                            {
                                intensities[scanNum - startScan][ibin - startBin] = decoded_intensityValue;
                            }
                            ibin++;
                        }
                    }
                }
                m_sqliteDataReader.Close();

            }

            return intensities;
        }

        // v1.2 caching methods
        /**
         * This method returns the mz values and the intensities as lists
         * */

        public int SumScansNonCached(List<double> mzs, List<int> intensities, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {

            if ((start_frame_index > end_frame_index) || (startScan > endScan))
            {
                throw new Exception("Please check whether startFrame < endFrame and startScan < endScan");
            }

            GlobalParameters gp = GetGlobalParameters();
            List<int> binValues = new List<int>(gp.Bins);
            int returnCount = SumScansNonCached(binValues, intensities, frameType, start_frame_index, end_frame_index, startScan, endScan);

            //now convert each of the bin values to mz values
            try
            {
                for (int i = 0; i < binValues.Count; i++)
                {
                    FrameParameters fp = GetFrameParameters(start_frame_index++);
                    mzs.Add(convertBinToMZ(fp.CalibrationSlope, fp.CalibrationIntercept, gp.BinWidth, gp.TOFCorrectionTime, binValues[i]));

                }
            }
            catch (NullReferenceException ne)
            {
                throw new Exception("Some of the frame parameters are missing ");
            }

            return returnCount;
        }


        /**
         * This method returns the bin values and the intensities as lists
         * */

        public int SumScansNonCached(List<int> bins, List<int> intensities, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {

            if (bins == null)
            {
                bins = new List<int>(m_globalParameters.Bins);
            }

            if (intensities == null)
            {
                intensities = new List<int>(m_globalParameters.Bins);
            }

            Dictionary<int, int> binsDict = new Dictionary<int, int>(m_globalParameters.Bins);
            if (start_frame_index < 0)
            {
                throw new Exception("StartFrame should be a positive integer");
            }

            m_sumScansCommand.Parameters.Add(new SQLiteParameter("FrameNum1", this.array_FrameNum[start_frame_index]));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("FrameNum2", this.array_FrameNum[end_frame_index]));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("ScanNum1", startScan));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("ScanNum2", endScan));
            m_sqliteDataReader = m_sumScansCommand.ExecuteReader();
            byte[] spectra;
            byte[] decomp_SpectraRecord = new byte[m_globalParameters.Bins * DATASIZE];

            int nonZeroCount = 0;
            int frame_index = start_frame_index;
            while (m_sqliteDataReader.Read())
            {
                int ibin = 0;
                int max_bin_iscan = 0;
                int out_len;
                spectra = (byte[])(m_sqliteDataReader["Intensities"]);

                //get frame number so that we can get the frame calibration parameters
                if (spectra.Length > 0)
                {

                    frame_index = this.get_FrameIndex(Convert.ToInt32(m_sqliteDataReader["FrameNum"]));
                    if (frame_index < 0)
                        continue;

                    FrameParameters fp = GetFrameParameters(frame_index);

                    out_len = IMSCOMP_wrapper.decompress_lzf(ref spectra, spectra.Length, ref decomp_SpectraRecord, m_globalParameters.Bins * DATASIZE);
                    int numBins = out_len / DATASIZE;
                    int decoded_SpectraRecord;
                    for (int i = 0; i < numBins; i++)
                    {
                        decoded_SpectraRecord = BitConverter.ToInt32(decomp_SpectraRecord, i * DATASIZE);
                        if (decoded_SpectraRecord < 0)
                        {
                            ibin += -decoded_SpectraRecord;
                        }
                        else
                        {

                            if (binsDict.ContainsKey(ibin))
                            {
                                binsDict[ibin] += decoded_SpectraRecord;
                            }
                            else
                            {
                                binsDict.Add(ibin, decoded_SpectraRecord);
                            }
                            if (max_bin_iscan < ibin) max_bin_iscan = ibin;

                            ibin++;
                        }
                    }
                    if (nonZeroCount < max_bin_iscan) nonZeroCount = max_bin_iscan;
                }
            }

            foreach (KeyValuePair<int, int> entry in binsDict)
            {
                // do something with entry.Value or entry.Key
                bins.Add(entry.Key);
                intensities.Add(entry.Value);
            }


            m_sumScansCommand.Parameters.Clear();
            m_sqliteDataReader.Close();
            if (nonZeroCount > 0) nonZeroCount++;
            return nonZeroCount;

        }

        public int SumScansNonCached(double[] mzs, int[] intensities, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {

            if (start_frame_index < 0)
            {
                throw new Exception("StartFrame should be a positive integer");
            }

            m_sumScansCommand.Parameters.Add(new SQLiteParameter("FrameNum1", this.array_FrameNum[start_frame_index]));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("FrameNum2", this.array_FrameNum[end_frame_index]));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("ScanNum1", startScan));
            m_sumScansCommand.Parameters.Add(new SQLiteParameter("ScanNum2", endScan));
            m_sqliteDataReader = m_sumScansCommand.ExecuteReader();
            byte[] spectra;
            byte[] decomp_SpectraRecord = new byte[m_globalParameters.Bins * DATASIZE];

            int nonZeroCount = 0;
            int frame_index = start_frame_index;
            while (m_sqliteDataReader.Read())
            {
                try
                {
                    int ibin = 0;
                    int max_bin_iscan = 0;
                    int out_len;
                    spectra = (byte[])(m_sqliteDataReader["Intensities"]);

                    //get frame number so that we can get the frame calibration parameters
                    if (spectra.Length > 0)
                    {

                        frame_index = this.get_FrameIndex(Convert.ToInt32(m_sqliteDataReader["FrameNum"]));
                        if (frame_index < 0)
                            continue;

                        FrameParameters fp = GetFrameParameters(frame_index);

                        out_len = IMSCOMP_wrapper.decompress_lzf(ref spectra, spectra.Length, ref decomp_SpectraRecord, m_globalParameters.Bins * DATASIZE);
                        int numBins = out_len / DATASIZE;
                        int decoded_SpectraRecord;
                        for (int i = 0; i < numBins; i++)
                        {
                            decoded_SpectraRecord = BitConverter.ToInt32(decomp_SpectraRecord, i * DATASIZE);
                            if (decoded_SpectraRecord < 0)
                            {
                                ibin += -decoded_SpectraRecord;
                            }
                            else
                            {

                                intensities[ibin] += decoded_SpectraRecord;
                                if (mzs[ibin] == 0.0D)
                                {
                                    mzs[ibin] = convertBinToMZ(fp.CalibrationSlope, fp.CalibrationIntercept, m_globalParameters.BinWidth, m_globalParameters.TOFCorrectionTime, ibin);
                                }
                                if (max_bin_iscan < ibin) max_bin_iscan = ibin;
                                ibin++;
                            }
                        }
                        if (nonZeroCount < max_bin_iscan) nonZeroCount = max_bin_iscan;
                    }
                }
                catch (IndexOutOfRangeException outOfRange)
                {
                    //Console.WriteLine("Error thrown when summing scans.  Error details: " + outOfRange.Message);

                    //do nothing, the bin numbers were outside the range 
                }

            }
            m_sumScansCommand.Parameters.Clear();
            m_sqliteDataReader.Close();

            if (nonZeroCount > 0) nonZeroCount++;
            return nonZeroCount;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mzs">Returned mz values</param>
        /// <param name="intensities">Returned intensities</param>
        /// <param name="frameType">Type of frames to sum</param>
        /// <param name="midFrame">Center frame for sliding window</param>
        /// <param name="range">Range of sliding window</param>
        /// <param name="startScan">Start scan number</param>
        /// <param name="endScan">End scan number</param>
        /// <returns></returns>
        public int SumScansRange(double[] mzs, int[] intensities, int frameType, int mid_frame_index, int range, int startScan, int endScan)
        {
            //Determine the start frame number and the end frame number for this range
            int counter = 0;

            this.set_FrameType(frameType);
            FrameParameters fp = GetFrameParameters(mid_frame_index);

            int start_frame_index = mid_frame_index - range;
            if (start_frame_index < 0)
                start_frame_index = 0;
            int end_frame_index = mid_frame_index + range;
            if (end_frame_index >= this.get_NumFrames(frameType))
                end_frame_index = this.get_NumFrames(frameType) - 1;

            counter = SumScansNonCached(mzs, intensities, frameType, start_frame_index, end_frame_index, startScan, endScan);

            //else, maybe we generate a warning but not sure
            return counter;
        }

        /// <summary>
        /// Method to check if this dataset has any MSMS data
        /// </summary>
        /// <returns>True if MSMS frames are present</returns>
        public bool hasMSMSData()
        {
            int current_frame_type = this.filtered_FrameType;

            int frag_frames = set_FrameType(2);

            bool hasMSMS = (frag_frames > 0 ? true : false);
            this.set_FrameType(current_frame_type);

            return hasMSMS;
        }


        // point the old SumScans methods to the cached version.
        public int SumScans(double[] mzs, int[] intensities, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {
            if ((start_frame_index < 0) || (end_frame_index >= this.get_NumFrames(frameType)))
            {
                throw new Exception("SumScans(): StartFrame should be a positive integer less than num_frames = " + this.get_NumFrames(frameType).ToString());
            }

            return SumScansNonCached(mzs, intensities, frameType, start_frame_index, end_frame_index, startScan, endScan);
        }

        // AARON: There is a lot of room for improvement in these methods.
        public int SumScans(double[] mzs, int[] intensities, int frameType, int start_frame_index, int end_frame_index, int scanNum)
        {
            int startScan = scanNum;
            int endScan = scanNum;
            int max_bin = SumScans(mzs, intensities, frameType, start_frame_index, end_frame_index, startScan, endScan);
            return max_bin;
        }

        public int SumScans(double[] mzs, int[] intensities, int frameType, int start_frame_index, int end_frame_index)
        {
            int startScan = 0;
            int endScan = 0;
            for (int iframe = start_frame_index; iframe <= end_frame_index; iframe++)
            {
                FrameParameters fp = GetFrameParameters(iframe);
                int iscan = fp.Scans - 1;
                if (endScan < iscan) endScan = iscan;
            }
            int max_bin = SumScans(mzs, intensities, frameType, start_frame_index, end_frame_index, startScan, endScan);
            return max_bin;
        }

        public int SumScans(double[] mzs, int[] intensities, int frameType, int frame_index)
        {
            int startScan = 0;
            int endScan = 0;

            FrameParameters fp = GetFrameParameters(frame_index);
            int iscan = fp.Scans - 1;
            if (endScan < iscan) endScan = iscan;

            int max_bin = SumScans(mzs, intensities, frameType, frame_index, frame_index, startScan, endScan);
            return max_bin;
        }

        public int SumScans(double[] mzs, double[] intensities, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {
            int[] intInts = new int[intensities.Length];

            int maxBin = SumScans(mzs, intInts, frameType, start_frame_index, end_frame_index, startScan, endScan);

            for (int i = 0; i < intensities.Length; i++)
            {
                intensities[i] = intInts[i];
            }

            return maxBin;
        }

        public int SumScans(double[] mzs, double[] intensities, int frameType, int start_frame_index, int end_frame_index, int scanNum)
        {
            return SumScans(mzs, intensities, frameType, start_frame_index, end_frame_index, scanNum, scanNum);
        }

        public int SumScans(double[] mzs, double[] intensities, int frameType, int start_frame_index, int end_frame_index)
        {
            if ((start_frame_index < 0) || (end_frame_index >= this.get_NumFrames(frameType)))
            {
                throw new Exception("SumScans(): StartFrame should be a positive integer less than num_frames = " + this.get_NumFrames(frameType).ToString());
            }

            int startScan = 0;
            int endScan = 0;
            for (int iframe = start_frame_index; iframe <= end_frame_index; iframe++)
            {
                FrameParameters fp = GetFrameParameters(iframe);
                int iscan = fp.Scans - 1;
                if (endScan < iscan) endScan = iscan;
            }
            int max_bin = SumScans(mzs, intensities, frameType, start_frame_index, end_frame_index, startScan, endScan);
            return max_bin;

        }

        public int SumScans(double[] mzs, double[] intensities, int frameType, int frame_index)
        {
            int max_bin = SumScans(mzs, intensities, frameType, frame_index, frame_index);
            return max_bin;
        }



        public int SumScans(double[] mzs, float[] intensities, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {
            int[] intIntensities = new int[intensities.Length];
            int max_bin = SumScans(mzs, intIntensities, frameType, start_frame_index, end_frame_index, startScan, endScan);

            for (int i = 0; i < intIntensities.Length; i++)
            {
                intensities[i] = intIntensities[i];
            }

            return max_bin;
        }

        public int SumScans(double[] mzs, float[] intensities, int frameType, int start_frame_index, int end_frame_index, int scanNum)
        {

            int max_bin = SumScans(mzs, intensities, frameType, start_frame_index, end_frame_index, scanNum, scanNum);
            return max_bin;
        }

        public int SumScans(double[] mzs, float[] intensities, int frameType, int start_frame_index, int end_frame_index)
        {
            int startScan = 0;
            int endScan = 0;
            for (int iframe = start_frame_index; iframe <= end_frame_index; iframe++)
            {
                FrameParameters fp = GetFrameParameters(iframe);
                int iscan = fp.Scans - 1;
                if (endScan < iscan) endScan = iscan;
            }
            int max_bin = SumScans(mzs, intensities, frameType, start_frame_index, end_frame_index, startScan, endScan);
            return max_bin;
        }

        public int SumScans(double[] mzs, float[] intensities, int frameType, int frame_index)
        {
            int startScan = 0;
            int endScan = 0;
            FrameParameters fp = GetFrameParameters(frame_index);
            int iscan = fp.Scans - 1;
            if (endScan < iscan) endScan = iscan;

            int max_bin = SumScans(mzs, intensities, frameType, frame_index, frame_index, startScan, endScan);
            return max_bin;
        }

        public int SumScans(double[] mzs, short[] intensities, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {
            if ((start_frame_index < 0) || (end_frame_index >= this.get_NumFrames(frameType)))
            {
                throw new Exception("SumScans(): StartFrame should be a positive integer less than num_frames = " + this.get_NumFrames(frameType).ToString());
            }

            int[] intInts = new int[intensities.Length];
            int maxBin = SumScans(mzs, intInts, frameType, start_frame_index, end_frame_index, startScan, endScan);
            for (int i = 0; i < intensities.Length; i++)
            {
                intensities[i] = (short)intInts[i];
            }
            return maxBin;
        }

        public int SumScans(double[] mzs, short[] intensities, int frameType, int start_frame_index, int end_frame_index, int scanNum)
        {
            return SumScans(mzs, intensities, frameType, start_frame_index, end_frame_index, scanNum, scanNum);
        }

        public int SumScans(double[] mzs, short[] intensities, int frameType, int start_frame_index, int end_frame_index)
        {
            if ((start_frame_index < 0) || (end_frame_index >= this.get_NumFrames(frameType)))
            {
                throw new Exception("SumScans(): start_frame_index should be a positive integer less than num_frames = " + this.get_NumFrames(frameType).ToString());
            }

            int startScan = 0;
            int endScan = 0;
            for (int iframe = start_frame_index; iframe <= end_frame_index; iframe++)
            {
                FrameParameters fp = GetFrameParameters(iframe);
                int iscan = fp.Scans - 1;
                if (endScan < iscan) endScan = iscan;
            }
            int max_bin = SumScans(mzs, intensities, frameType, start_frame_index, end_frame_index, startScan, endScan);
            return max_bin;
        }

        public int SumScans(double[] mzs, short[] intensities, int frameType, int frame_index)
        {
            int start_frame_index = frame_index;
            int end_frame_index = frame_index;
            int startScan = 0;
            FrameParameters fp = GetFrameParameters(frame_index);
            return SumScans(mzs, intensities, frameType, start_frame_index, end_frame_index, startScan, fp.Scans - 1);
        }

        // This function extracts BPI from startFrame to endFrame and startScan to endScan
        // and returns an array BPI[]
        public void GetBPI(double[] bpi, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {
            GetTICorBPI(bpi, frameType, start_frame_index, end_frame_index, startScan, endScan, BPI);
        }

        // This function extracts TIC from startFrame to endFrame and startScan to endScan
        // and returns an array TIC[]
        public void GetTIC(double[] tic, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {
            GetTICorBPI(tic, frameType, start_frame_index, end_frame_index, startScan, endScan, TIC);
        }

        public void GetTIC(double[] TIC, int frameType)
        {
            //this should return the TIC for the entire experiment
            int start_frame_index = 0;
            int end_frame_index = this.get_NumFrames(this.filtered_FrameType);

            GetTIC(TIC, frameType, start_frame_index, end_frame_index);
        }

        public void GetTIC(double[] TIC, int frameType, int start_frame_index, int end_frame_index)
        {
            //That means we have to sum all scans
            //First iterate through all frames to find the max end scan:
            //This is done since we are expecting different number of scans per frame
            //if that was not the case then we could just do away with seraching for any frame
            int startScan = 0;
            int endScan = 0;
            for (int i = start_frame_index; i < end_frame_index; i++)
            {
                FrameParameters fp = GetFrameParameters(i);
                if (endScan < fp.Scans)
                {
                    endScan = fp.Scans;
                }
            }
            GetTIC(TIC, frameType, start_frame_index, end_frame_index, startScan, endScan);

        }

        public void GetTIC(float[] TIC, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {

            double[] data = new double[1];
            GetTICorBPI(data, frameType, start_frame_index, end_frame_index, startScan, endScan, "TIC");

            if (TIC == null || TIC.Length < data.Length)
            {
                TIC = new float[data.Length];
            }

            for (int i = 0; i < data.Length; i++)
            {
                TIC[i] = Convert.ToSingle(data[i]);
            }

        }

        public void GetTIC(int[] TIC, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {

            double[] data = new double[1];
            GetTICorBPI(data, frameType, start_frame_index, end_frame_index, startScan, endScan, "TIC");

            if (TIC == null || TIC.Length < data.Length)
            {
                TIC = new int[data.Length];
            }

            for (int i = 0; i < data.Length; i++)
            {
                TIC[i] = Convert.ToInt32(data[i]);
            }

        }

        public void GetTIC(short[] TIC, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan)
        {

            double[] data;

            data = new double[1];
            GetTICorBPI(data, frameType, start_frame_index, end_frame_index, startScan, endScan, "TIC");

            if (TIC == null || TIC.Length < data.Length)
            {
                TIC = new short[data.Length];
            }

            for (int i = 0; i < data.Length; i++)
            {
                TIC[i] = Convert.ToInt16(data[i]);
            }

        }
        // This function extracts TIC from frameNum adn scanNum
        // This function extracts TIC from frameNum and scanNum
        public double GetTIC(int frame_index, int scanNum)
        {
            double tic = 0;
            if (frame_index < 0)
            {
                throw new Exception("frameNum must be a positive integer. frame=" + frame_index.ToString());
            }

            SQLiteCommand dbCmd = m_uimfDatabaseConnection.CreateCommand();
            dbCmd.CommandText = "SELECT TIC FROM Frame_Scans WHERE FrameNum = " + this.array_FrameNum[frame_index] + " AND ScanNum = " + scanNum;
            SQLiteDataReader reader = dbCmd.ExecuteReader();

            if (reader.Read())
            {
                tic = Convert.ToDouble(reader["TIC"]);
            }

            Dispose(dbCmd, reader);
            return tic;
        }

        // This function extracts intensities from frameNum and scanNum,
        // and returns number of non-zero intensities found in this spectrum and two arrays spectrum[] and mzs[]
        public int GetSpectrum(int frame_index, int scanNum, double[] spectrum, double[] mzs)
        {
            int nNonZero = 0;
            int[] intSpec = new int[spectrum.Length];

            nNonZero = GetSpectrum(frame_index, scanNum, intSpec, mzs);
            for (int i = 0; i < intSpec.Length; i++)
            {
                spectrum[i] = intSpec[i];
            }
            return nNonZero;
        }

        public int GetSpectrum(int frame_index, int scanNum, float[] spectrum, double[] mzs)
        {
            int nNonZero = 0;
            int[] intSpec = new int[spectrum.Length];

            nNonZero = GetSpectrum(frame_index, scanNum, intSpec, mzs);

            for (int i = 0; i < intSpec.Length; i++)
            {
                spectrum[i] = intSpec[i];
            }

            return nNonZero;
        }

        public int GetSpectrum(int frame_index, int scanNum, int[] spectrum, double[] mzs)
        {
            if (frame_index < 0 || scanNum < 0)
            {
                throw new Exception("frameNum should be a positive integer");
            }

            FrameParameters fp = GetFrameParameters(frame_index);
            SQLiteCommand dbCmd = m_uimfDatabaseConnection.CreateCommand();
            dbCmd.CommandText = "SELECT Intensities FROM Frame_Scans WHERE FrameNum = " + this.array_FrameNum[frame_index] + " AND ScanNum = " + scanNum;
            m_sqliteDataReader = dbCmd.ExecuteReader();
            int nNonZero = 0;
            int expectedCount = GetCountPerSpectrum(frame_index, scanNum);
            byte[] SpectraRecord;
            byte[] decomp_SpectraRecord = new byte[expectedCount * DATASIZE * 5];//this is the maximum possible size, again we should

            int ibin = 0;
            while (m_sqliteDataReader.Read())
            {
                int out_len;
                SpectraRecord = (byte[])(m_sqliteDataReader["Intensities"]);
                if (SpectraRecord.Length > 0)
                {
                    out_len = IMSCOMP_wrapper.decompress_lzf(ref SpectraRecord, SpectraRecord.Length, ref decomp_SpectraRecord, m_globalParameters.Bins * DATASIZE);

                    int numBins = out_len / DATASIZE;
                    int decoded_SpectraRecord;
                    for (int i = 0; i < numBins; i++)
                    {
                        decoded_SpectraRecord = BitConverter.ToInt32(decomp_SpectraRecord, i * DATASIZE);
                        if (decoded_SpectraRecord < 0)
                        {
                            ibin += -decoded_SpectraRecord;
                        }
                        else
                        {
                            double t = (double)ibin * m_globalParameters.BinWidth / 1000;
                            double ResidualMassError = fp.a2 * t + fp.b2 * System.Math.Pow(t, 3) + fp.c2 * System.Math.Pow(t, 5) + fp.d2 * System.Math.Pow(t, 7) + fp.e2 * System.Math.Pow(t, 9) + fp.f2 * System.Math.Pow(t, 11);
                            mzs[nNonZero] = (double)(fp.CalibrationSlope * ((double)(t - (double)m_globalParameters.TOFCorrectionTime / 1000 - fp.CalibrationIntercept)));
                            mzs[nNonZero] = mzs[nNonZero] * mzs[nNonZero] + ResidualMassError;
                            spectrum[nNonZero] = decoded_SpectraRecord;
                            ibin++;
                            nNonZero++;
                        }
                    }
                }
            }

            Dispose(dbCmd, m_sqliteDataReader);
            return nNonZero;
        }

        public int GetSpectrum(int frame_index, int scanNum, short[] spectrum, double[] mzs)
        {
            int nNonZero = 0;
            int[] intSpec = new int[spectrum.Length];

            nNonZero = GetSpectrum(frame_index, scanNum, intSpec, mzs);

            for (int i = 0; i < intSpec.Length; i++)
            {
                spectrum[i] = (short)intSpec[i];
            }

            return nNonZero;
        }

        private double[] array_FragmentationSequence(byte[] blob)
        {
            // convert the array of bytes to an array of doubles
            double[] frag = new double[blob.Length / 8];

            for (int i = 0; i < frag.Length; i++)
                frag[i] = BitConverter.ToDouble(blob, i * 8);

            return frag;
        }

        private int CheckInputArguments(ref int frameType, int start_frame_index, ref int end_frame_index, ref int endScan, ref int endBin)
        {
            // This function checks input arguments and assign default values when some arguments are set to -1
            int NumFrames = this.get_NumFrames(this.filtered_FrameType);
            FrameParameters startFp = null;

            if ((start_frame_index < 0) || (end_frame_index >= NumFrames))
            {
                throw new Exception("CheckInputArguments(): start_frame_index should be a positive integer less than num_frames = " + this.get_NumFrames(frameType).ToString());
            }

            if (frameType == -1)
            {
                startFp = GetFrameParameters(start_frame_index);
                frameType = startFp.FrameType;
            }

            if (end_frame_index == -1)
            {
                end_frame_index = this.get_NumFrames(this.filtered_FrameType);
                int Frame_count = 0;
                for (int i = start_frame_index; i < end_frame_index + 1; i++)
                {
                    FrameParameters fp = GetFrameParameters(i);
                    int frameType_iframe = fp.FrameType;
                    if (frameType_iframe == frameType)
                    {
                        Frame_count++;
                    }
                }
                end_frame_index = start_frame_index + Frame_count - 1;
            }


            //This line could easily cause a null pointer exception since startFp is not defined. check this.
            if (endScan == -1) endScan = startFp.Scans - 1;

            int Num_Bins = m_globalParameters.Bins;
            if (endBin == -1) endBin = Num_Bins - 1;

            return Num_Bins;
        }

        // AARON: this has room for improvement, along with all the methods that use it.
        protected void GetTICorBPI(double[] data, int frameType, int start_frame_index, int end_frame_index, int startScan, int endScan, string FieldName)
        {
            if (start_frame_index < 0)
            {
                throw new Exception("StartFrame should be a positive integer");
            }

            // Make sure endFrame is valid
            if (end_frame_index < start_frame_index)
                end_frame_index = start_frame_index;

            // Compute the number of frames to be returned
            int nframes = end_frame_index - start_frame_index + 1;

            // Make sure TIC is initialized
            if (data == null || data.Length < nframes)
            {
                data = new double[nframes];
            }

            // Construct the SQL
            string SQL = " SELECT Frame_Scans.FrameNum, Sum(Frame_Scans." + FieldName + ") AS Value " +
                " FROM Frame_Scans" +
                " WHERE FrameNum >= " + this.array_FrameNum[start_frame_index] + " AND FrameNum <= " + this.array_FrameNum[end_frame_index];

            if (!(startScan == 0 && endScan == 0))
            {
                // Filter by scan number
                SQL += " AND Frame_Scans.ScanNum >= " + startScan + " AND Frame_Scans.ScanNum <= " + endScan;
            }

            SQL += " GROUP BY Frame_Scans.FrameNum ORDER BY Frame_Scans.FrameNum";

            SQLiteCommand dbcmd_UIMF = m_uimfDatabaseConnection.CreateCommand();
            dbcmd_UIMF.CommandText = SQL;
            SQLiteDataReader reader = dbcmd_UIMF.ExecuteReader();

            int ncount = 0;
            while (reader.Read())
            {
                data[ncount] = Convert.ToDouble(reader["Value"]);
                ncount++;
            }

            Dispose(dbcmd_UIMF, reader);
        }


        public void SumScansNonCached(List<ushort> list_frame_index, List<List<ushort>> scanNumbers, List<double> mzList, List<double> intensityList, double minMz, double maxMz)
        {
            SumScansNonCached(list_frame_index, scanNumbers, mzList, intensityList, minMz, maxMz);
        }

        public void SumScansNonCached(List<int> list_frame_index, List<List<int>> scanNumbers, List<double> mzList, List<double> intensityList, double minMz, double maxMz)
        {
            List<int> iList = new List<int>(m_globalParameters.Bins);

            SumScansNonCached(list_frame_index, scanNumbers, mzList, iList, minMz, maxMz);

            for (int i = 0; i < iList.Count; i++)
            {
                intensityList.Add(iList[i]);
            }
        }

        public void SumScansNonCached(List<int> list_frame_index, List<List<int>> scanNumbers, List<double> mzList, List<int> intensityList, double minMz, double maxMz)
        {
            int[][] scanNumbersArray = new int[list_frame_index.Count][];

            for (int i = 0; i < list_frame_index.Count; i++)
            {
                scanNumbersArray[i] = new int[scanNumbers[i].Count];
                for (int j = 0; j < scanNumbers[i].Count; j++)
                {
                    scanNumbersArray[i][j] = scanNumbers[i][j];
                }

            }
            int[] intensities = new int[GetGlobalParameters().Bins];

            SumScansNonCached(list_frame_index.ToArray(), scanNumbersArray, intensities);
            if (intensityList == null)
            {
                intensityList = new List<int>();
            }

            if (mzList == null)
            {
                mzList = new List<double>();
            }

            FrameParameters fp = GetFrameParameters(list_frame_index[0]);
            for (int i = 0; i < intensities.Length; i++)
            {
                if (intensities[i] > 0)
                {
                    double mz = convertBinToMZ(fp.CalibrationSlope, fp.CalibrationIntercept, m_globalParameters.BinWidth, m_globalParameters.TOFCorrectionTime, i);
                    if (minMz <= mz && mz <= maxMz)
                    {
                        mzList.Add(mz);
                        intensityList.Add(intensities[i]);
                    }
                }

            }

        }

        public void SumScansNonCached(int[] array_frame_index, int[][] scanNumbers, int[] intensities)
        {
            System.Text.StringBuilder commandText;

            //intensities = new int[m_globalParameters.Bins];

            //Iterate through each list element to get frame number
            for (int i = 0; i < array_frame_index.Length; i++)
            {
                commandText = new System.Text.StringBuilder("SELECT Intensities FROM Frame_Scans WHERE FrameNum = ");

                int frame_index = array_frame_index[i];
                commandText.Append(this.array_FrameNum[frame_index] + " AND ScanNum in (");


                for (int j = 0; j < scanNumbers[i].Length; j++)
                {
                    commandText.Append(scanNumbers[i][j].ToString() + ",");
                }

                //remove the last comma
                commandText.Remove(commandText.Length - 1, 1);
                commandText.Append(");");

                m_sumVariableScansPerFrameCommand.CommandText = commandText.ToString();
                SQLiteDataReader reader = m_sumVariableScansPerFrameCommand.ExecuteReader();
                byte[] spectra;
                byte[] decomp_SpectraRecord = new byte[m_globalParameters.Bins];

                while (reader.Read())
                {
                    try
                    {
                        int ibin = 0;
                        int out_len;

                        spectra = (byte[])(reader["Intensities"]);

                        //get frame number so that we can get the frame calibration parameters
                        if (spectra.Length > 0)
                        {
                            out_len = IMSCOMP_wrapper.decompress_lzf(ref spectra, spectra.Length, ref decomp_SpectraRecord, m_globalParameters.Bins);
                            int numBins = out_len / DATASIZE;
                            int decoded_intensityValue;
                            for (int ix = 0; ix < numBins; ix++)
                            {
                                decoded_intensityValue = BitConverter.ToInt32(decomp_SpectraRecord, ix * DATASIZE);
                                if (decoded_intensityValue < 0)
                                {
                                    ibin += -decoded_intensityValue;
                                }
                                else
                                {
                                    intensities[ibin] += decoded_intensityValue;
                                    ibin++;
                                }
                            }
                        }
                    }
                    catch (IndexOutOfRangeException e)
                    {
                        //do nothing
                    }
                    reader.Close();
                }
            }
        }


        public void GetFrameData(int frame_index, List<int> scanNumberList, List<int> bins, List<int> intensities, List<int> spectrumCountList)
        {
            m_sumScansCachedCommand.Parameters.Clear();
            m_sumScansCachedCommand.Parameters.Add(new SQLiteParameter(":FrameNum", this.array_FrameNum[frame_index]));
            SQLiteDataReader reader = m_sumScansCachedCommand.ExecuteReader();
            byte[] spectra = null;
            byte[] decomp_SpectraRecord = new byte[m_globalParameters.Bins];

            while (reader.Read())
            {
                int scanNum = Convert.ToInt32(reader["ScanNum"]);
                spectra = (byte[])(reader["Intensities"]);

                if (spectra.Length > 0)
                {
                    scanNumberList.Add(scanNum);

                    FrameParameters fp = GetFrameParameters(frame_index);

                    int out_len = IMSCOMP_wrapper.decompress_lzf(ref spectra, spectra.Length, ref decomp_SpectraRecord, m_globalParameters.Bins * DATASIZE);
                    int numBins = out_len / DATASIZE;
                    int decoded_SpectraRecord;
                    int nonZeroCount = 0;
                    int ibin = 0;
                    for (int i = 0; i < numBins; i++)
                    {
                        decoded_SpectraRecord = BitConverter.ToInt32(decomp_SpectraRecord, i * DATASIZE);
                        if (decoded_SpectraRecord < 0)
                        {
                            ibin += -decoded_SpectraRecord;
                        }
                        else
                        {
                            bins.Add(ibin);
                            intensities.Add(decoded_SpectraRecord);
                            nonZeroCount++;
                            ibin++;
                        }
                    }
                    spectrumCountList.Add(nonZeroCount);
                }
            }

            reader.Close();
        }

        public void SumScansForVariableRange(List<ushort> list_frame_index, List<List<ushort>> scanNumbers, int frameType, int[] intensities)
        {
            System.Text.StringBuilder commandText;
            //Iterate through each list element to get frame number
            for (int i = 0; i < list_frame_index.Count; i++)
            {
                commandText = new System.Text.StringBuilder("SELECT FrameNum, ScanNum, Intensities FROM Frame_Scans WHERE FrameNum = ");

                int frame_index = list_frame_index[i];
                commandText.Append(this.array_FrameNum[frame_index].ToString() + " AND ScanNum in (");
                List<ushort> correspondingScans = scanNumbers[i];

                for (int j = 0; j < correspondingScans.Count; j++)
                {
                    commandText.Append(correspondingScans[j].ToString() + ",");
                }

                //remove the last comma
                commandText.Remove(commandText.Length - 1, 1);
                commandText.Append(");");

                m_sumVariableScansPerFrameCommand.CommandText = commandText.ToString();
                m_sqliteDataReader = m_sumVariableScansPerFrameCommand.ExecuteReader();
                byte[] spectra;
                byte[] decomp_SpectraRecord = new byte[m_globalParameters.Bins];

                while (m_sqliteDataReader.Read())
                {
                    try
                    {
                        int ibin = 0;
                        int out_len;
                        spectra = (byte[])(m_sqliteDataReader["Intensities"]);
                        //get frame number so that we can get the frame calibration parameters
                        if (spectra.Length > 0)
                        {
                            out_len = IMSCOMP_wrapper.decompress_lzf(ref spectra, spectra.Length, ref decomp_SpectraRecord, m_globalParameters.Bins);
                            int numBins = out_len / DATASIZE;
                            int decoded_intensityValue;
                            for (int ix = 0; ix < numBins; ix++)
                            {
                                decoded_intensityValue = BitConverter.ToInt32(decomp_SpectraRecord, ix * DATASIZE);
                                if (decoded_intensityValue < 0)
                                {
                                    ibin += -decoded_intensityValue;
                                }
                                else
                                {
                                    intensities[ibin] += decoded_intensityValue;
                                    ibin++;
                                }
                            }
                        }
                    }
                    catch (IndexOutOfRangeException e)
                    {
                        //do nothing
                    }
                }
                m_sqliteDataReader.Close();
                //construct query
            }
        }



        public int[][] GetFramesAndScanIntensitiesForAGivenMz(int start_frame_index, int end_frame_index, int frameType, int startScan, int endScan, double targetMZ, double toleranceInMZ)
        {
            if ((start_frame_index > end_frame_index) || (start_frame_index < 0))
            {
                throw new System.ArgumentException("Failed to get 3D profile. Input startFrame was greater than input endFrame");
            }

            if (startScan > endScan || startScan < 0)
            {
                throw new System.ArgumentException("Failed to get 3D profile. Input startScan was greater than input endScan");
            }

            int[][] intensityValues = new int[end_frame_index - start_frame_index + 1][];
            int[] lowerUpperBins = GetUpperLowerBinsFromMz(start_frame_index, targetMZ, toleranceInMZ);

            int[][][] frameIntensities = GetIntensityBlock(start_frame_index, end_frame_index, frameType, startScan, endScan, lowerUpperBins[0], lowerUpperBins[1]);


            for (int frame_index = start_frame_index; frame_index <= end_frame_index; frame_index++)
            {
                intensityValues[frame_index - start_frame_index] = new int[endScan - startScan + 1];
                for (int scan = startScan; scan <= endScan; scan++)
                {

                    int sumAcrossBins = 0;
                    for (int bin = lowerUpperBins[0]; bin <= lowerUpperBins[1]; bin++)
                    {
                        int binIntensity = frameIntensities[frame_index - start_frame_index][scan - startScan][bin - lowerUpperBins[0]];
                        sumAcrossBins += binIntensity;
                    }

                    intensityValues[frame_index - start_frame_index][scan - startScan] = sumAcrossBins;

                }
            }

            return intensityValues;
        }



        /// <summary>
        /// Returns the x,y,z arrays needed for a surface plot for the elution of the species in both the LC and drifttime dimensions
        /// </summary>
        /// <param name="startFrame"></param>
        /// <param name="endFrame"></param>
        /// <param name="frameType"></param>
        /// <param name="startScan"></param>
        /// <param name="endScan"></param>
        /// <param name="targetMZ"></param>
        /// <param name="toleranceInMZ"></param>
        /// <param name="frameValues"></param>
        /// <param name="scanValues"></param>
        /// <param name="intensities"></param>
        public void Get3DElutionProfile(int start_frame_index, int end_frame_index, int frameType, int startScan, int endScan, double targetMZ, double toleranceInMZ, ref int[] frameValues, ref int[] scanValues, ref int[] intensities)
        {

            if ((start_frame_index > end_frame_index) || (start_frame_index < 0))
            {
                throw new System.ArgumentException("Failed to get LCProfile. Input startFrame was greater than input endFrame. start_frame=" + start_frame_index.ToString() + ", end_frame=" + end_frame_index.ToString());
            }

            if (startScan > endScan)
            {
                throw new System.ArgumentException("Failed to get 3D profile. Input startScan was greater than input endScan");
            }

            int lengthOfOutputArrays = (end_frame_index - start_frame_index + 1) * (endScan - startScan + 1);

            frameValues = new int[lengthOfOutputArrays];
            scanValues = new int[lengthOfOutputArrays];
            intensities = new int[lengthOfOutputArrays];


            int[] lowerUpperBins = GetUpperLowerBinsFromMz(start_frame_index, targetMZ, toleranceInMZ);

            int[][][] frameIntensities = GetIntensityBlock(start_frame_index, end_frame_index, frameType, startScan, endScan, lowerUpperBins[0], lowerUpperBins[1]);

            int counter = 0;

            for (int frame_index = start_frame_index; frame_index <= end_frame_index; frame_index++)
            {
                for (int scan = startScan; scan <= endScan; scan++)
                {
                    int sumAcrossBins = 0;
                    for (int bin = lowerUpperBins[0]; bin <= lowerUpperBins[1]; bin++)
                    {
                        int binIntensity = frameIntensities[frame_index - start_frame_index][scan - startScan][bin - lowerUpperBins[0]];
                        sumAcrossBins += binIntensity;
                    }
                    frameValues[counter] = frame_index;
                    scanValues[counter] = scan;
                    intensities[counter] = sumAcrossBins;
                    counter++;
                }
            }
        }

        public void GetLCProfile(int start_frame_index, int end_frame_index, int frameType, int startScan, int endScan, double targetMZ, double toleranceInMZ, ref int[] frameValues, ref int[] intensities)
        {
            if ((start_frame_index > end_frame_index) || (start_frame_index < 0))
            {
                throw new System.ArgumentException("Failed to get LCProfile. Input startFrame was greater than input endFrame. start_frame=" + start_frame_index.ToString() + ", end_frame=" + end_frame_index.ToString());
            }

            frameValues = new int[end_frame_index - start_frame_index + 1];

            int[] lowerAndUpperBinBoundaries = GetUpperLowerBinsFromMz(start_frame_index, targetMZ, toleranceInMZ);
            intensities = new int[end_frame_index - start_frame_index + 1];

            int[][][] frameIntensities = GetIntensityBlock(start_frame_index, end_frame_index, frameType, startScan, endScan, lowerAndUpperBinBoundaries[0], lowerAndUpperBinBoundaries[1]);
            for (int frame_index = start_frame_index; frame_index <= end_frame_index; frame_index++)
            {
                int scanSum = 0;
                for (int scan = startScan; scan <= endScan; scan++)
                {
                    int binSum = 0;
                    for (int bin = lowerAndUpperBinBoundaries[0]; bin <= lowerAndUpperBinBoundaries[1]; bin++)
                    {
                        binSum += frameIntensities[frame_index - start_frame_index][scan - startScan][bin - lowerAndUpperBinBoundaries[0]];
                    }
                    scanSum += binSum;
                }

                intensities[frame_index - start_frame_index] = scanSum;
                frameValues[frame_index - start_frame_index] = frame_index;
            }
        }


        public void GetDriftTimeProfile(int startFrameIndex, int endFrameIndex, int frameType, int startScan, int endScan, double targetMZ, double toleranceInMZ, ref int[] imsScanValues, ref int[] intensities)
        {
            if ((startFrameIndex > endFrameIndex) || (startFrameIndex < 0))
            {
                throw new System.ArgumentException("Failed to get DriftTime profile. Input startFrame was greater than input endFrame. start_frame=" + startFrameIndex.ToString() + ", end_frame=" + endFrameIndex.ToString());
            }

            if ((startScan > endScan) || (startScan < 0))
            {
                throw new System.ArgumentException("Failed to get LCProfile. Input startScan was greater than input endScan. startScan=" + startScan + ", endScan=" + endScan);
            }

            int lengthOfScanArray = endScan - startScan + 1;
            imsScanValues = new int[lengthOfScanArray];
            intensities = new int[lengthOfScanArray];

            int[] lowerAndUpperBinBoundaries = GetUpperLowerBinsFromMz(startFrameIndex, targetMZ, toleranceInMZ);

            int[][][] intensityBlock = GetIntensityBlock(startFrameIndex, endFrameIndex, frameType, startScan, endScan, lowerAndUpperBinBoundaries[0], lowerAndUpperBinBoundaries[1]);

            for (int scanIndex = startScan; scanIndex <= endScan; scanIndex++)
            {
                int frameSum = 0;
                for (int frameIndex = startFrameIndex; frameIndex <= endFrameIndex; frameIndex++)
                {
                    int binSum = 0;
                    for (int bin = lowerAndUpperBinBoundaries[0]; bin <= lowerAndUpperBinBoundaries[1]; bin++)
                    {
                        binSum += intensityBlock[frameIndex - startFrameIndex][scanIndex - startScan][bin - lowerAndUpperBinBoundaries[0]];
                    }
                    frameSum += binSum;

                }

                intensities[scanIndex - startScan] = frameSum;
                imsScanValues[scanIndex - startScan] = scanIndex;



            }

        }


        private int[] GetUpperLowerBinsFromMz(int frame_index, double targetMZ, double toleranceInMZ)
        {
            int[] bins = new int[2];
            double lowerMZ = targetMZ - toleranceInMZ;
            double upperMZ = targetMZ + toleranceInMZ;
            FrameParameters fp = GetFrameParameters(frame_index);
            GlobalParameters gp = this.GetGlobalParameters();
            bool polynomialCalibrantsAreUsed = (fp.a2 != 0 || fp.b2 != 0 || fp.c2 != 0 || fp.d2 != 0 || fp.e2 != 0 || fp.f2 != 0);
            if (polynomialCalibrantsAreUsed)
            {
                //note: the reason for this is that we are trying to get the closest bin for a given m/z.  But when a polynomial formula is used to adjust the m/z, it gets
                // much more complicated.  So someone else can figure that out  :)
                throw new NotImplementedException("DriftTime profile extraction hasn't been implemented for UIMF files containing polynomial calibration constants.");
            }

            double lowerBin = getBinClosestToMZ(fp.CalibrationSlope, fp.CalibrationIntercept, gp.BinWidth, gp.TOFCorrectionTime, lowerMZ);
            double upperBin = getBinClosestToMZ(fp.CalibrationSlope, fp.CalibrationIntercept, gp.BinWidth, gp.TOFCorrectionTime, upperMZ);
            bins[0] = (int)Math.Round(lowerBin, 0);
            bins[1] = (int)Math.Round(upperBin, 0);
            return bins;
        }
   
        /// <summary>
        /// Returns the bin value that corresponds to an m/z value.  
        /// NOTE: this may not be accurate if the UIMF file uses polynomial calibration values  (eg.  FrameParameter A2)
        /// </summary>
        /// <param name="slope"></param>
        /// <param name="intercept"></param>
        /// <param name="binWidth"></param>
        /// <param name="correctionTimeForTOF"></param>
        /// <param name="targetMZ"></param>
        /// <returns></returns>
        public double getBinClosestToMZ(double slope, double intercept, double binWidth, double correctionTimeForTOF, double targetMZ)
        {
            //NOTE: this may not be accurate if the UIMF file uses polynomial calibration values  (eg.  FrameParameter A2)
            double binCorrection = (correctionTimeForTOF / 1000) / binWidth;
            double bin = (Math.Sqrt(targetMZ) / slope + intercept) / binWidth * 1000;
            //TODO:  have a test case with a TOFCorrectionTime > 0 and verify the binCorrection adjustment
            return bin + binCorrection;
        }

        public void UpdateCalibrationCoefficients(int frame_index, float slope, float intercept)
        {
            dbcmd_PreparedStmt = m_uimfDatabaseConnection.CreateCommand();
            dbcmd_PreparedStmt.CommandText = "UPDATE Frame_Parameters SET CalibrationSlope = " + slope.ToString() +
                ", CalibrationIntercept = " + intercept.ToString() + " WHERE FrameNum = " + this.array_FrameNum[frame_index].ToString();

            dbcmd_PreparedStmt.ExecuteNonQuery();
            dbcmd_PreparedStmt.Dispose();
        }

        private void Dispose(SQLiteCommand cmd, SQLiteDataReader reader)
        {
            cmd.Dispose();
            reader.Dispose();
            reader.Close();
        }

        private double convertBinToMZ(double slope, double intercept, double binWidth, double correctionTimeForTOF, int bin)
        {
            double t = bin * binWidth / 1000;
            double term1 = (double)(slope * ((t - correctionTimeForTOF / 1000 - intercept)));
            return term1 * term1;
        }

        public Stack<int[]> GetFrameAndScanListByDescendingIntensity()
        {
            FrameParameters fp = GetFrameParameters(0);
            Stack<int[]> tuples = new Stack<int[]>(this.get_NumFrames(this.filtered_FrameType) * fp.Scans);
            int[] values = new int[3];

            m_sqliteDataReader = m_getFramesAndScanByDescendingIntensityCommand.ExecuteReader();
            while (m_sqliteDataReader.Read())
            {
                values = new int[3];
                values[0] = Convert.ToInt32(m_sqliteDataReader[0]);
                values[1] = Convert.ToInt32(m_sqliteDataReader[1]);
                values[2] = Convert.ToInt32(m_sqliteDataReader[2]);

                tuples.Push(values);
            }
            m_sqliteDataReader.Close();
            return tuples;
        }


        // //////////////////////////////////////////////////////////////////////////////////////
        // //////////////////////////////////////////////////////////////////////////////////////
        // //////////////////////////////////////////////////////////////////////////////////////
        // Viewer functionality
        // 
        // William Danielson
        // //////////////////////////////////////////////////////////////////////////////////////
        // //////////////////////////////////////////////////////////////////////////////////////
        // //////////////////////////////////////////////////////////////////////////////////////
        //
        public void update_CalibrationCoefficients(int frame_index, float slope, float intercept)
        {
            if (frame_index > this.array_FrameNum.Length)
                return;

            dbcmd_PreparedStmt = m_uimfDatabaseConnection.CreateCommand();
            dbcmd_PreparedStmt.CommandText = "UPDATE Frame_Parameters SET CalibrationSlope = " + slope.ToString() +
                ", CalibrationIntercept = " + intercept.ToString() + " WHERE FrameNum = " + this.array_FrameNum[frame_index].ToString();

            dbcmd_PreparedStmt.ExecuteNonQuery();
            dbcmd_PreparedStmt.Dispose();

            this.mz_Calibration.k = slope / 10000.0;
            this.mz_Calibration.t0 = intercept * 10000.0;

            this.reset_FrameParameters();
        }

        public void updateAll_CalibrationCoefficients(float slope, float intercept)
        {
            dbcmd_PreparedStmt = m_uimfDatabaseConnection.CreateCommand();
            dbcmd_PreparedStmt.CommandText = "UPDATE Frame_Parameters SET CalibrationSlope = " + slope.ToString() +
                ", CalibrationIntercept = " + intercept.ToString();

            dbcmd_PreparedStmt.ExecuteNonQuery();
            dbcmd_PreparedStmt.Dispose();

            /*
            this.mz_Calibration.k = slope / 10000.0;
            this.mz_Calibration.t0 = intercept * 10000.0;
            */

            this.reset_FrameParameters();
        }

        public void reset_FrameParameters()
        {
            this.m_frameParametersCache.Clear();
            this.GetFrameParameters(this.current_FrameIndex);
        }

        public int get_FrameIndex(int frame_number)
        {
            return Array.BinarySearch(this.array_FrameNum, frame_number);
            /*
            for (int i = 0; i < this.array_FrameNum.Length; i++)
                if (frame_number == this.array_FrameNum[i])
                    return i;

            return -1;
             */
        }
        public int get_FrameType()
        {
            return filtered_FrameType;
        }
        public int set_FrameType(int frame_type)
        {
            int frame_count;
            int i;

            if (this.filtered_FrameType == frame_type)
                return this.array_FrameNum.Length;

            this.filtered_FrameType = frame_type;

            this.dbcmd_PreparedStmt = this.m_uimfDatabaseConnection.CreateCommand();
            this.dbcmd_PreparedStmt.CommandText = "SELECT COUNT(FrameNum) FROM Frame_Parameters WHERE FrameType = " + this.filtered_FrameType.ToString();
            this.m_sqliteDataReader = this.dbcmd_PreparedStmt.ExecuteReader();

            frame_count = Convert.ToInt32(this.m_sqliteDataReader[0]);
            this.m_sqliteDataReader.Dispose();

            if (frame_count == 0)
            {
                this.array_FrameNum = new int[0];
                return 0;
            }

            // build an array of frame numbers for instant referencing.
            this.array_FrameNum = new int[frame_count];
            this.dbcmd_PreparedStmt.Dispose();

            this.dbcmd_PreparedStmt = this.m_uimfDatabaseConnection.CreateCommand();
            this.dbcmd_PreparedStmt.CommandText = "SELECT FrameNum FROM Frame_Parameters WHERE FrameType = " + this.filtered_FrameType.ToString() + " ORDER BY FrameNum ASC";

            this.m_sqliteDataReader = this.dbcmd_PreparedStmt.ExecuteReader();

            i = 0;
            while (this.m_sqliteDataReader.Read())
            {
                this.array_FrameNum[i] = Convert.ToInt32(this.m_sqliteDataReader[0]);
                i++;
            }

            this.m_frameParameters = this.GetFrameParameters(0);
            this.dbcmd_PreparedStmt.Dispose();

            return frame_count;
        }

        public int load_Frame(int index)
        {
            if ((index < this.array_FrameNum.Length) && (index > 0))
            {
                this.m_frameParameters = this.GetFrameParameters(index);
                return this.array_FrameNum[index];
            }
            else
                return -1;
        }

        public int get_NumFrames(int frametype)
        {
            return this.set_FrameType(frametype);
        }

        public double get_pixelMZ(int bin)
        {
            if ((calibration_table != null) && (bin < calibration_table.Length))
                return calibration_table[bin];
            else
                return -1;
        }

        public double TenthsOfNanoSecondsPerBin
        {
            get { return (double)(this.m_globalParameters.BinWidth * 10.0); }
        }

        public int[][] accumulate_FrameData(int frame_index, bool flag_TOF, int start_scan, int start_bin, int[][] frame_data, int y_compression)
        {
            return this.accumulate_FrameData(frame_index, flag_TOF, start_scan, start_bin, 0, this.m_globalParameters.Bins, frame_data, y_compression);
        }

        public int[][] accumulate_FrameData(int frame_index, bool flag_TOF, int start_scan, int start_bin, int min_mzbin, int max_mzbin, int[][] frame_data, int y_compression)
        {
            if ((frame_index < 0) || (frame_index >= this.array_FrameNum.Length))
                return frame_data;

            int i;

            int data_width = frame_data.Length;
            int data_height = frame_data[0].Length;

            byte[] compressed_BinIntensity;
            byte[] stream_BinIntensity = new byte[this.m_globalParameters.Bins * 4];
            int scans_data;
            int index_current_bin;
            int bin_data;
            int int_BinIntensity;
            int decompress_length;
            int pixel_y = 0;
            int current_scan;
            int bin_value;
            int end_bin;

            if (y_compression > 0)
                end_bin = start_bin + (data_height * y_compression);
            else if (y_compression < 0)
                end_bin = start_bin + data_height - 1;
            else
            {
                throw new Exception("UIMFLibrary accumulate_PlotData: Compression == 0");
                return frame_data;
            }

            // Create a calibration lookup table -- for speed
            this.calibration_table = new double[data_height];
            if (flag_TOF)
            {
                for (i = 0; i < data_height; i++)
                    this.calibration_table[i] = start_bin + ((double)i * (double)(end_bin - start_bin) / (double)data_height);
            }
            else
            {
                double mz_min = (double)this.mz_Calibration.TOFtoMZ((float)((start_bin / this.m_globalParameters.BinWidth) * TenthsOfNanoSecondsPerBin));
                double mz_max = (double)this.mz_Calibration.TOFtoMZ((float)((end_bin / this.m_globalParameters.BinWidth) * TenthsOfNanoSecondsPerBin));

                for (i = 0; i < data_height; i++)
                    this.calibration_table[i] = (double)this.mz_Calibration.MZtoTOF(mz_min + ((double)i * (mz_max - mz_min) / (double)data_height)) * this.m_globalParameters.BinWidth / (double)TenthsOfNanoSecondsPerBin;
            }

            // ensure the correct Frame parameters are set
            if (this.array_FrameNum[frame_index] != this.m_frameParameters.FrameNum)
            {
                this.m_frameParameters = (UIMFLibrary.FrameParameters)this.GetFrameParameters(frame_index);
            }

            // This function extracts intensities from selected scans and bins in a single frame 
            // and returns a two-dimetional array intensities[scan][bin]
            // frameNum is mandatory and all other arguments are optional
            this.dbcmd_PreparedStmt = this.m_uimfDatabaseConnection.CreateCommand();
            this.dbcmd_PreparedStmt.CommandText = "SELECT ScanNum, Intensities FROM Frame_Scans WHERE FrameNum = " + this.array_FrameNum[frame_index].ToString() + " AND ScanNum >= " + start_scan.ToString() + " AND ScanNum <= " + (start_scan + data_width - 1).ToString();

            this.m_sqliteDataReader = this.dbcmd_PreparedStmt.ExecuteReader();
            this.dbcmd_PreparedStmt.Dispose();

            // accumulate the data into the plot_data
            if (y_compression < 0)
            {
                pixel_y = 1;

                //MessageBox.Show(start_bin.ToString() + " " + end_bin.ToString());

                for (scans_data = 0; ((scans_data < data_width) && this.m_sqliteDataReader.Read()); scans_data++)
                {
                    current_scan = Convert.ToInt32(this.m_sqliteDataReader["ScanNum"]) - start_scan;
                    compressed_BinIntensity = (byte[])(this.m_sqliteDataReader["Intensities"]);

                    if (compressed_BinIntensity.Length == 0)
                        continue;

                    index_current_bin = 0;
                    decompress_length = UIMFLibrary.IMSCOMP_wrapper.decompress_lzf(ref compressed_BinIntensity, compressed_BinIntensity.Length, ref stream_BinIntensity, this.m_globalParameters.Bins * 4);

                    for (bin_data = 0; (bin_data < decompress_length) && (index_current_bin <= end_bin); bin_data += 4)
                    {
                        int_BinIntensity = BitConverter.ToInt32(stream_BinIntensity, bin_data);

                        if (int_BinIntensity < 0)
                        {
                            index_current_bin += -int_BinIntensity;   // concurrent zeros
                        }
                        else if ((index_current_bin < min_mzbin) || (index_current_bin < start_bin))
                            index_current_bin++;
                        else if (index_current_bin > max_mzbin)
                            break;
                        else
                        {
                            frame_data[current_scan][index_current_bin - start_bin] += int_BinIntensity;
                            index_current_bin++;
                        }
                    }
                }
            }
            else    // each pixel accumulates more than 1 bin of data
            {
                for (scans_data = 0; ((scans_data < data_width) && this.m_sqliteDataReader.Read()); scans_data++)
                {
                    current_scan = Convert.ToInt32(this.m_sqliteDataReader["ScanNum"]) - start_scan;
                    // if (current_scan >= data_width)
                    //     break;

                    compressed_BinIntensity = (byte[])(this.m_sqliteDataReader["Intensities"]);

                    if (compressed_BinIntensity.Length == 0)
                        continue;

                    index_current_bin = 0;
                    decompress_length = UIMFLibrary.IMSCOMP_wrapper.decompress_lzf(ref compressed_BinIntensity, compressed_BinIntensity.Length, ref stream_BinIntensity, this.m_globalParameters.Bins * 4);

                    pixel_y = 1;

                    double calibrated_bin = 0;
                    for (bin_value = 0; (bin_value < decompress_length) && (index_current_bin < end_bin); bin_value += 4)
                    {
                        int_BinIntensity = BitConverter.ToInt32(stream_BinIntensity, bin_value);

                        if (int_BinIntensity < 0)
                        {
                            index_current_bin += -int_BinIntensity; // concurrent zeros
                        }
                        else if ((index_current_bin < min_mzbin) || (index_current_bin < start_bin))
                            index_current_bin++;
                        else if (index_current_bin > max_mzbin)
                            break;
                        else
                        {
                            calibrated_bin = (double)index_current_bin;

                            for (i = pixel_y; i < data_height; i++)
                            {
                                if (calibration_table[i] > calibrated_bin)
                                {
                                    pixel_y = i;
                                    frame_data[current_scan][pixel_y] += int_BinIntensity;
                                    break;
                                }
                            }
                            index_current_bin++;
                        }
                    }
                }
            }

            this.m_sqliteDataReader.Close();
            return frame_data;
        }

        public int[] get_MobilityData(int frame_index)
        {
            return get_MobilityData(frame_index, 0, this.m_globalParameters.Bins);
        }

        public int[] get_MobilityData(int frame_index, int min_mzbin, int max_mzbin)
        {
            int[] mobility_data = new int[0];
            int mobility_index;
            byte[] compressed_BinIntensity;
            byte[] stream_BinIntensity = new byte[this.m_globalParameters.Bins * 4];
            int current_scan;
            int int_BinIntensity;
            int decompress_length;
            int bin_index;
            int index_current_bin;

            try
            {
                this.load_Frame(frame_index);
                mobility_data = new int[this.m_frameParameters.Scans];

                // This function extracts intensities from selected scans and bins in a single frame 
                // and returns a two-dimetional array intensities[scan][bin]
                // frameNum is mandatory and all other arguments are optional
                this.dbcmd_PreparedStmt = this.m_uimfDatabaseConnection.CreateCommand();
                this.dbcmd_PreparedStmt.CommandText = "SELECT ScanNum, Intensities FROM Frame_Scans WHERE FrameNum = " + this.array_FrameNum[frame_index].ToString();// +" AND ScanNum >= " + start_scan.ToString() + " AND ScanNum <= " + (start_scan + data_width).ToString();

                this.m_sqliteDataReader = this.dbcmd_PreparedStmt.ExecuteReader();
                this.dbcmd_PreparedStmt.Dispose();

                for (mobility_index = 0; ((mobility_index < this.m_frameParameters.Scans) && this.m_sqliteDataReader.Read()); mobility_index++)
                {
                    current_scan = Convert.ToInt32(this.m_sqliteDataReader["ScanNum"]);
                    compressed_BinIntensity = (byte[])(this.m_sqliteDataReader["Intensities"]);

                    if ((compressed_BinIntensity.Length == 0) || (current_scan >= this.m_frameParameters.Scans))
                        continue;

                    index_current_bin = 0;
                    decompress_length = UIMFLibrary.IMSCOMP_wrapper.decompress_lzf(ref compressed_BinIntensity, compressed_BinIntensity.Length, ref stream_BinIntensity, this.m_globalParameters.Bins * 4);

                    for (bin_index = 0; (bin_index < decompress_length); bin_index += 4)
                    {
                        int_BinIntensity = BitConverter.ToInt32(stream_BinIntensity, bin_index);

                        if (int_BinIntensity < 0)
                        {
                            index_current_bin += -int_BinIntensity;   // concurrent zeros
                        }
                        else if (index_current_bin < min_mzbin)
                            index_current_bin++;
                        else if (index_current_bin > max_mzbin)
                            break;
                        else
                        {
                            try
                            {
                                mobility_data[current_scan] += int_BinIntensity;
                            }
                            catch (Exception ex)
                            {
                                throw new Exception(mobility_index.ToString() + "  " + current_scan.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("get_MobilityData: \n\n" + ex.ToString());
            }

            return mobility_data;
        }
    }
}
