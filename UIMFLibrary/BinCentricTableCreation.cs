﻿// --------------------------------------------------------------------------------------------------------------------
// <summary>
//   Bin centric table creation.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace UIMFLibrary
{
	using System;
	using System.Collections.Generic;
	using System.Data.SQLite;
	using System.IO;

	/// <summary>
	/// The bin centric table creation.
	/// </summary>
	public class BinCentricTableCreation
	{
		#region Constants

		/// <summary>
		/// Command for creating the Bin_intensities index
		/// </summary>
		public const string CREATE_BINS_INDEX = "CREATE INDEX Bin_Intensities_MZ_BIN_IDX ON Bin_Intensities(MZ_BIN);";

		/// <summary>
		/// Command for creating the Bin_intensities table
		/// </summary>
		public const string CREATE_BINS_TABLE = "CREATE TABLE Bin_Intensities (MZ_BIN int(11), INTENSITIES BLOB);";

		/// <summary>
		/// Command for adding a row to the Bin_Intensities table
		/// </summary>
		public const string INSERT_BIN_INTENSITIES =
			"INSERT INTO Bin_Intensities (MZ_BIN, INTENSITIES) VALUES(:MZ_BIN, :INTENSITIES)";

		/// <summary>
		/// Bin size
		/// </summary>
		private const int BIN_SIZE = 200;

		#endregion

		#region Public Events

        public event EventHandler<MessageEventArgs> OnError;

		/// <summary>
		/// Message event handler.
		/// </summary>
		/// <param name="sender">
		/// Message sender
		/// </param>
		/// <param name="e">
		/// Message event args
		/// </param>
        public event EventHandler<MessageEventArgs> Message;

		/// <summary>
		/// Progress event handler.
		/// </summary>
		/// <param name="sender">
		/// Progress sender
		/// </param>
		/// <param name="e">
		/// Progress event args
		/// </param>
        public event EventHandler<ProgressEventArgs> OnProgress;

		#endregion

		#region Public Methods and Operators

		/// <summary>
		/// Create the bin centric table.
		/// </summary>
		/// <param name="uimfWriterConnection">
		/// UIMF Writer connection
		/// </param>
		/// <param name="uimfReader">
		/// UIMF Reader connection
		/// </param>
		public void CreateBinCentricTable(SQLiteConnection uimfWriterConnection, DataReader uimfReader)
		{
			this.CreateBinCentricTable(uimfWriterConnection, uimfReader, string.Empty);
		}

		/// <summary>
		/// Create the bin centric table.
		/// </summary>
		/// <param name="uimfWriterConnection">
		/// UIMF Writer connection
		/// </param>
		/// <param name="uimfReader">
		/// UIMF Reader connection
		/// </param>
		/// <param name="workingDirectory">
		/// Working directory
		/// </param>
		public void CreateBinCentricTable(
			SQLiteConnection uimfWriterConnection, 
			DataReader uimfReader, 
			string workingDirectory)
		{
			// Create the temporary database
			string temporaryDatabaseLocation = this.CreateTemporaryDatabase(uimfReader, workingDirectory);

			// Note: providing true for parseViaFramework as a workaround for reading SqLite files located on UNC or in readonly folders
			string connectionString = "Data Source=" + temporaryDatabaseLocation + ";";
			using (var temporaryDatabaseConnection = new SQLiteConnection(connectionString, true))
			{
				temporaryDatabaseConnection.Open();

				// Write the bin centric tables to UIMF file
				this.InsertBinCentricData(uimfWriterConnection, temporaryDatabaseConnection, uimfReader);
			}

			// Delete the temporary database
			try
			{
				File.Delete(temporaryDatabaseLocation);
			}
			catch
			{
				// Ignore deletion errors
			}
		}

		/// <summary>
		/// Raise the error event
		/// </summary>
		/// <param name="e">
		/// Message event args
		/// </param>
		public void OnErrorMessage(MessageEventArgs e)
		{
            var errorEvent = this.OnError;
            if (errorEvent != null)
			{
                errorEvent(this, e);
			}
		}

		/// <summary>
		/// Raise the message event
		/// </summary>
		/// <param name="e">
		/// Message event args
		/// </param>
		public void OnMessage(MessageEventArgs e)
		{
            var messageEvent = this.Message;
            if (messageEvent != null)
			{
                messageEvent(this, e);
			}
		}

		/// <summary>
		/// Raise the progress event
		/// </summary>
		/// <param name="e">
		/// Message event args
		/// </param>
		public void OnProgressUpdate(ProgressEventArgs e)
		{
            var progressUpdate = this.OnProgress;
            if (progressUpdate != null)
			{
                progressUpdate(this, e);
			}
		}

		#endregion

		#region Methods

		/// <summary>
		/// Create the bin intensities index.
		/// </summary>
		/// <param name="uimfWriterConnection">
		/// UIMF writer
		/// </param>
		private void CreateBinIntensitiesIndex(SQLiteConnection uimfWriterConnection)
		{
			using (SQLiteCommand command = new SQLiteCommand(CREATE_BINS_INDEX, uimfWriterConnection))
			{
				command.ExecuteNonQuery();
			}
		}

		/// <summary>
		/// Create the bin intensities table.
		/// </summary>
		/// <param name="uimfWriterConnection">
		/// UIMF writer
		/// </param>
		private void CreateBinIntensitiesTable(SQLiteConnection uimfWriterConnection)
		{
			using (SQLiteCommand command = new SQLiteCommand(CREATE_BINS_TABLE, uimfWriterConnection))
			{
				command.ExecuteNonQuery();
			}
		}

		/// <summary>
		/// Create a blank database.
		/// </summary>
		/// <param name="locationForNewDatabase">
		/// File path for the new database.
		/// </param>
		/// <param name="numBins">
		/// Number of bins
		/// </param>
		/// <returns>
		/// Number of tables created<see cref="int"/>.
		/// </returns>
		private int CreateBlankDatabase(string locationForNewDatabase, int numBins)
		{
			// Create new SQLite file
			var sqliteFile = new FileInfo(locationForNewDatabase);
			if (sqliteFile.Exists)
			{
				sqliteFile.Delete();
			}

			string connectionString = "Data Source=" + sqliteFile.FullName + ";";

			int tablesCreated = 0;

			// Note: providing true for parseViaFramework as a workaround for reading SqLite files located on UNC or in readonly folders
			using (var connection = new SQLiteConnection(connectionString, true))
			{
				connection.Open();

				for (int i = 0; i <= numBins; i += BIN_SIZE)
				{
					using (var sqlCommand = new SQLiteCommand(this.GetCreateIntensitiesTableQuery(i), connection))
					{
						sqlCommand.ExecuteNonQuery();
					}

					tablesCreated++;
				}
			}

			return tablesCreated;
		}

		/// <summary>
		/// Create the indices
		/// </summary>
		/// <param name="locationForNewDatabase">
		/// File path for the new database.
		/// </param>
		/// <param name="numBins">
		/// Number of bins
		/// </param>
		private void CreateIndexes(string locationForNewDatabase, int numBins)
		{
			var sqliteFile = new FileInfo(locationForNewDatabase);
			string connectionString = "Data Source=" + sqliteFile.FullName + ";";

			using (var connection = new SQLiteConnection(connectionString, true))
			{
				connection.Open();

				for (int i = 0; i <= numBins; i += BIN_SIZE)
				{
					using (var sqlCommand = new SQLiteCommand(this.GetCreateIndexesQuery(i), connection))
					{
						sqlCommand.ExecuteNonQuery();
					}

					if (numBins > 0)
					{
						// Note: We are assuming that 37% of the time was taken up by CreateTemporaryDatabase, 30% by CreateIndexes, and 33% by InsertBinCentricData
						string progressMessage = "Creating indices, Bin: " + i + " / " + numBins;
						double percentComplete = 37 + (i / (double)numBins) * 30;
						this.UpdateProgress(percentComplete, progressMessage);
					}
				}
			}
		}

		/// <summary>
		/// Create the temporary database.
		/// </summary>
		/// <param name="uimfReader">
		/// UIMF reader
		/// </param>
		/// <param name="workingDirectory">
		/// Working directory path
		/// </param>
		/// <returns>
		/// Full path to the SqLite temporary database<see cref="string"/>.
		/// </returns>
		/// <exception cref="IOException">
		/// </exception>
		private string CreateTemporaryDatabase(DataReader uimfReader, string workingDirectory)
		{
			FileInfo uimfFileInfo = new FileInfo(uimfReader.UimfFilePath);

			// Get location of new SQLite file
			string sqliteFileName = uimfFileInfo.Name.Replace(".UIMF", "_temporary.db3").Replace(".uimf", "_temporary.db3");
			FileInfo sqliteFile = new FileInfo(Path.Combine(workingDirectory, sqliteFileName));

			if (uimfFileInfo.FullName.ToLower() == sqliteFile.FullName.ToLower())
			{
				throw new IOException(
					"Cannot add bin-centric tables, temporary SqLite file has the same name as the source SqLite file: "
					+ uimfFileInfo.FullName);
			}

			Console.WriteLine(DateTime.Now + " - Writing " + sqliteFile.FullName);

			// Create new SQLite file
			if (File.Exists(sqliteFile.FullName))
			{
				File.Delete(sqliteFile.FullName);
			}

			string connectionString = "Data Source=" + sqliteFile.FullName + ";";

			// Get global UIMF information
			GlobalParameters globalParameters = uimfReader.GetGlobalParameters();
			int numFrames = globalParameters.NumFrames;
			int numBins = globalParameters.Bins;

			int tablesCreated = this.CreateBlankDatabase(sqliteFile.FullName, numBins);

			using (var connection = new SQLiteConnection(connectionString, true))
			{
				connection.Open();

				var commandDictionary = new Dictionary<int, SQLiteCommand>();

				for (int i = 0; i <= numBins; i += BIN_SIZE)
				{
					string query = this.GetInsertIntensityQuery(i);
					var sqlCommand = new SQLiteCommand(query, connection);
					sqlCommand.Prepare();
					commandDictionary.Add(i, sqlCommand);
				}

				using (SQLiteTransaction transaction = connection.BeginTransaction())
				{
					for (int frameNumber = 1; frameNumber <= numFrames; frameNumber++)
					{
						string progressMessage = "Processing Frame: " + frameNumber + " / " + numFrames;
						Console.WriteLine(DateTime.Now + " - " + progressMessage);

						FrameParameters frameParameters = uimfReader.GetFrameParameters(frameNumber);
						int numScans = frameParameters.Scans;

						// Get data from UIMF file
						Dictionary<int, int>[] frameBinData = uimfReader.GetIntensityBlockOfFrame(frameNumber);

						for (int scanNumber = 0; scanNumber < numScans; scanNumber++)
						{
							Dictionary<int, int> scanData = frameBinData[scanNumber];

							foreach (KeyValuePair<int, int> kvp in scanData)
							{
								int binNumber = kvp.Key;
								int intensity = kvp.Value;
								int modValue = binNumber % BIN_SIZE;
								int minBin = binNumber - modValue;

								SQLiteCommand sqlCommand = commandDictionary[minBin];
								SQLiteParameterCollection parameters = sqlCommand.Parameters;
								parameters.Clear();
								parameters.Add(new SQLiteParameter(":MZ_BIN", binNumber));
								parameters.Add(new SQLiteParameter(":SCAN_LC", frameNumber));
								parameters.Add(new SQLiteParameter(":SCAN_IMS", scanNumber));
								parameters.Add(new SQLiteParameter(":INTENSITY", intensity));
								sqlCommand.ExecuteNonQuery();
							}
						}

						// Note: We are assuming that 37% of the time was taken up by CreateTemporaryDatabase, 30% by CreateIndexes, and 33% by InsertBinCentricData
						double percentComplete = 0 + (frameNumber / (double)numFrames) * 37;
						this.UpdateProgress(percentComplete, progressMessage);
					}

					transaction.Commit();
				}
			}

			Console.WriteLine(DateTime.Now + " - Indexing " + tablesCreated + " tables");

			this.CreateIndexes(sqliteFile.FullName, numBins);

			Console.WriteLine(DateTime.Now + " - Done populating temporary DB");

			return sqliteFile.FullName;
		}

		/// <summary>
		/// Create the indices for a given bin
		/// </summary>
		/// <param name="binNumber">
		/// Bin number.
		/// </param>
		/// <returns>
		/// Query for creating a Bin_Intensities index<see cref="string"/>.
		/// </returns>
		private string GetCreateIndexesQuery(int binNumber)
		{
			int minBin, maxBin;
			this.GetMinAndMaxBin(binNumber, out minBin, out maxBin);

			return "CREATE INDEX Bin_Intensities_" + minBin + "_" + maxBin + "_MZ_BIN_SCAN_LC_SCAN_IMS_IDX ON Bin_Intensities_"
			       + minBin + "_" + maxBin + " (MZ_BIN, SCAN_LC, SCAN_IMS);";
		}

		/// <summary>
		/// Create the intensities table for a given bin
		/// </summary>
		/// <param name="binNumber">
		/// Bin number.
		/// </param>
		/// <returns>
		/// Query for creating a Bin_Intensities table<see cref="string"/>.
		/// </returns>
		private string GetCreateIntensitiesTableQuery(int binNumber)
		{
			int minBin, maxBin;
			this.GetMinAndMaxBin(binNumber, out minBin, out maxBin);

			return "CREATE TABLE Bin_Intensities_" + minBin + "_" + maxBin + " (" + "MZ_BIN    int(11)," + "SCAN_LC    int(11),"
			       + "SCAN_IMS   int(11)," + "INTENSITY  int(11));";
		}

		/// <summary>
		/// Get intensities for a given bin
		/// </summary>
		/// <param name="binNumber">
		/// Bin number
		/// </param>
		/// <returns>
		/// Query for insert into a Bin_Intensities table <see cref="string"/>.
		/// </returns>
		private string GetInsertIntensityQuery(int binNumber)
		{
			int minBin, maxBin;
			this.GetMinAndMaxBin(binNumber, out minBin, out maxBin);

			return "INSERT INTO Bin_Intensities_" + minBin + "_" + maxBin + " (MZ_BIN, SCAN_LC, SCAN_IMS, INTENSITY)"
			       + "VALUES (:MZ_BIN, :SCAN_LC, :SCAN_IMS, :INTENSITY);";
		}

		/// <summary>
		/// Get the min and max bin numbers
		/// </summary>
		/// <param name="binNumber">
		/// Bin number
		/// </param>
		/// <param name="minBin">
		/// Output: minimum bin index
		/// </param>
		/// <param name="maxBin">
		/// Output: maximum bin index
		/// </param>
		private void GetMinAndMaxBin(int binNumber, out int minBin, out int maxBin)
		{
			int modValue = binNumber % BIN_SIZE;
			minBin = binNumber - modValue;
			maxBin = binNumber + (BIN_SIZE - modValue - 1);
		}

		/// <summary>
		/// Get the statement for reading intensities for a given bin
		/// </summary>
		/// <param name="binNumber">
		/// Bin number
		/// </param>
		/// <returns>
		/// Query for obtaining intensities for a single bin<see cref="string"/>.
		/// </returns>
		private string GetReadSingleBinQuery(int binNumber)
		{
			int minBin, maxBin;
			this.GetMinAndMaxBin(binNumber, out minBin, out maxBin);

			return "SELECT * FROM Bin_Intensities_" + minBin + "_" + maxBin + " WHERE MZ_BIN = " + binNumber
			       + " ORDER BY SCAN_LC, SCAN_IMS;";
		}

		/// <summary>
		/// Insert bin centric data.
		/// </summary>
		/// <param name="uimfWriterConnection">
		/// UIMF Writer object
		/// </param>
		/// <param name="temporaryDatabaseConnection">
		/// Temporary database connection.
		/// </param>
		/// <param name="uimfReader">
		/// UIMF reader object
		/// </param>
		private void InsertBinCentricData(
			SQLiteConnection uimfWriterConnection, 
			SQLiteConnection temporaryDatabaseConnection, 
			DataReader uimfReader)
		{
			int numBins = uimfReader.GetGlobalParameters().Bins;
			int numImsScans = uimfReader.GetFrameParameters(1).Scans;

			string targetFile = uimfWriterConnection.ConnectionString;
			int charIndex = targetFile.IndexOf(";");
			if (charIndex > 0)
			{
				targetFile = targetFile.Substring(0, charIndex - 1).Trim();
			}

			Console.WriteLine(DateTime.Now + " - Adding bin-centric data to " + targetFile);
			DateTime dtLastProgress = DateTime.UtcNow;

			// Create new table in the UIMF file that will be used to store bin-centric data
			this.CreateBinIntensitiesTable(uimfWriterConnection);

			using (SQLiteCommand insertCommand = new SQLiteCommand(INSERT_BIN_INTENSITIES, uimfWriterConnection))
			{
				insertCommand.Prepare();

				for (int i = 0; i <= numBins; i++)
				{
					this.SortDataForBin(temporaryDatabaseConnection, insertCommand, i, numImsScans);

					if (DateTime.UtcNow.Subtract(dtLastProgress).TotalSeconds >= 5)
					{
						string progressMessage = "Processing Bin: " + i + " / " + numBins;
						Console.WriteLine(DateTime.Now + " - " + progressMessage);
						dtLastProgress = DateTime.UtcNow;

						// Note: We are assuming that 37% of the time was taken up by CreateTemporaryDatabase, 30% by CreateIndexes, and 33% by InsertBinCentricData
						double percentComplete = (37 + 30) + (i / (double)numBins) * 33;
						this.UpdateProgress(percentComplete, progressMessage);
					}
				}
			}

			this.CreateBinIntensitiesIndex(uimfWriterConnection);

			Console.WriteLine(DateTime.Now + " - Done");
		}

		/// <summary>
		/// Sort data for bin.
		/// </summary>
		/// <param name="inConnection">
		/// Sqlite connection
		/// </param>
		/// <param name="insertCommand">
		/// Insert command
		/// </param>
		/// <param name="binNumber">
		/// Bin number
		/// </param>
		/// <param name="numImsScans">
		/// Number of IMS scans
		/// </param>
		private void SortDataForBin(
			SQLiteConnection inConnection, 
			SQLiteCommand insertCommand, 
			int binNumber, 
			int numImsScans)
		{
			List<int> runLengthZeroEncodedData = new List<int>();
			insertCommand.Parameters.Clear();

			string query = this.GetReadSingleBinQuery(binNumber);

			using (SQLiteCommand readCommand = new SQLiteCommand(query, inConnection))
			{
				using (SQLiteDataReader reader = readCommand.ExecuteReader())
				{
					int previousLocation = 0;

					while (reader.Read())
					{
						int scanLc = Convert.ToInt32(reader[1]);
						int scanIms = Convert.ToInt32(reader[2]);
						int intensity = Convert.ToInt32(reader[3]);

						int newLocation = (scanLc * numImsScans) + scanIms;
						int difference = newLocation - previousLocation - 1;

						// Add the negative difference if greater than 0 to represent a number of scans without data
						if (difference > 0)
						{
							runLengthZeroEncodedData.Add(-difference);
						}

						// Add the intensity value for this particular scan
						runLengthZeroEncodedData.Add(intensity);

						previousLocation = newLocation;
					}
				}
			}

			int dataCount = runLengthZeroEncodedData.Count;

			if (dataCount > 0)
			{
				// byte[] compressedRecord = new byte[dataCount * 4 * 5];
				byte[] byteBuffer = new byte[dataCount * 4];
				Buffer.BlockCopy(runLengthZeroEncodedData.ToArray(), 0, byteBuffer, 0, dataCount * 4);

				// int nlzf = LZFCompressionUtil.Compress(ref byteBuffer, dataCount * 4, ref compressedRecord, compressedRecord.Length);
				// byte[] spectra = new byte[nlzf];
				// Array.Copy(compressedRecord, spectra, nlzf);
				insertCommand.Parameters.Add(new SQLiteParameter(":MZ_BIN", binNumber));
				insertCommand.Parameters.Add(new SQLiteParameter(":INTENSITIES", byteBuffer));

				insertCommand.ExecuteNonQuery();
			}
		}

		/// <summary>
		/// Update progress.
		/// </summary>
		/// <param name="percentComplete">
		/// Percent complete.
		/// </param>
		private void UpdateProgress(double percentComplete)
		{
			this.OnProgressUpdate(new ProgressEventArgs(percentComplete));
		}

		/// <summary>
		/// Update progress.
		/// </summary>
		/// <param name="percentComplete">
		/// Percent complete.
		/// </param>
		/// <param name="currentTask">
		/// Current task.
		/// </param>
		private void UpdateProgress(double percentComplete, string currentTask)
		{
			this.OnProgressUpdate(new ProgressEventArgs(percentComplete));

			if (!string.IsNullOrEmpty(currentTask))
			{
				this.OnMessage(new MessageEventArgs(currentTask));
			}
		}

		#endregion
	}
}