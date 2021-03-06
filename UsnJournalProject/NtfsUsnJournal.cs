﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using PInvoke;

namespace UsnJournal
{
   public class NtfsUsnJournal : IDisposable
   {
      public enum UsnJournalReturnCode
      {
         INVALID_HANDLE_VALUE = -1,
         USN_JOURNAL_SUCCESS = 0,
         //ERROR_INVALID_FUNCTION = 1,
         //ERROR_FILE_NOT_FOUND = 2,
         //ERROR_PATH_NOT_FOUND = 3,
         //ERROR_TOO_MANY_OPEN_FILES = 4,
         //ERROR_ACCESS_DENIED = 5,
         //ERROR_INVALID_HANDLE = 6,
         //ERROR_INVALID_DATA = 13,
         ERROR_HANDLE_EOF = 38,
         //ERROR_NOT_SUPPORTED = 50,
         //ERROR_INVALID_PARAMETER = 87,
         //ERROR_JOURNAL_DELETE_IN_PROGRESS = 1178,
         USN_JOURNAL_NOT_ACTIVE = 1179,
         //ERROR_JOURNAL_ENTRY_DELETED = 1181,
         //ERROR_INVALID_USER_BUFFER = 1784,
         //USN_JOURNAL_INVALID = 17001,
         VOLUME_NOT_NTFS = 17003,
         //INVALID_FILE_REFERENCE_NUMBER = 17004,
         USN_JOURNAL_ERROR = 17005
      }


      //public enum UsnReasonCode
      //{
      //   USN_REASON_DATA_OVERWRITE = 1,
      //   USN_REASON_DATA_EXTEND = 2,
      //   USN_REASON_DATA_TRUNCATION = 4,
      //   USN_REASON_NAMED_DATA_OVERWRITE = 16,
      //   USN_REASON_NAMED_DATA_EXTEND = 32,
      //   USN_REASON_NAMED_DATA_TRUNCATION = 64,
      //   USN_REASON_FILE_CREATE = 256,
      //   USN_REASON_FILE_DELETE = 512,
      //   USN_REASON_EA_CHANGE = 1024,
      //   USN_REASON_SECURITY_CHANGE = 2048,
      //   USN_REASON_RENAME_OLD_NAME = 4096,
      //   USN_REASON_RENAME_NEW_NAME = 8192,
      //   USN_REASON_INDEXABLE_CHANGE = 16384,
      //   USN_REASON_BASIC_INFO_CHANGE = 32768,
      //   USN_REASON_HARD_LINK_CHANGE = 65536,
      //   USN_REASON_COMPRESSION_CHANGE = 131072,
      //   USN_REASON_ENCRYPTION_CHANGE = 262144,
      //   USN_REASON_OBJECT_ID_CHANGE = 524288,
      //   USN_REASON_REPARSE_POINT_CHANGE = 1048576,
      //   USN_REASON_STREAM_CHANGE = 2097152,
      //   USN_REASON_CLOSE = -1
      //}
      
      
      private readonly DriveInfo _driveInfo;
      private readonly bool bNtfsVolume;
      private IntPtr _usnJournalRootHandle;

      public static TimeSpan ElapsedTime { get; private set; }

      public string VolumeName { get; private set; }


      /// <summary>Initializes an NtfsUsnJournal instance. If no exception is thrown, _usnJournalRootHandle and
      /// _volumeSerialNumber can be assumed to be good. If an exception is thrown, the NtfsUsnJournal object is not usable.</summary>
      /// <param name="drive">Local drive letter that provides access to information about a volume</param>
      /// <remarks>An exception thrown if the volume is not an NTFS volume or if GetRootHandle() or GetVolumeSerialNumber() functions fail. 
      /// Each public method checks to see if the volume is NTFS and if the _usnJournalRootHandle is valid.
      /// If these two conditions aren't met, then the public function will return a UsnJournalReturnCode  error.</remarks>
      public NtfsUsnJournal(string drive) : this(new DriveInfo(drive))
      {
      }


      /// <summary>Initializes an NtfsUsnJournal instance. If no exception is thrown, _usnJournalRootHandle and
      /// _volumeSerialNumber can be assumed to be good. If an exception is thrown, the NtfsUsnJournal object is not usable.</summary>
      /// <param name="driveInfo">DriveInfo object that provides access to information about a volume</param>
      /// <remarks>An exception thrown if the volume is not an NTFS volume or if GetRootHandle() or GetVolumeSerialNumber() functions fail. 
      /// Each public method checks to see if the volume is NTFS and if the _usnJournalRootHandle is valid.
      /// If these two conditions aren't met, then the public function will return a UsnJournalReturnCode  error.</remarks>
      public NtfsUsnJournal(DriveInfo driveInfo)
      {
         _driveInfo = driveInfo;
         VolumeName = driveInfo.Name;

         bNtfsVolume = _driveInfo.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);

         var sw = new Stopwatch();
         sw.Start();

         if (bNtfsVolume)
         {
            IntPtr rootHandle;

            var lastError = GetRootHandle(out rootHandle);
            if (lastError == 0)
            {
               _usnJournalRootHandle = rootHandle;

               uint volumeSerialNumber;
               lastError = GetVolumeSerialNumber(_driveInfo, out volumeSerialNumber);
               if (lastError != 0)
               {
                  ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);
                  throw new Win32Exception(lastError);
               }
            }

            else
            {
               ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);
               throw new Win32Exception(lastError);
            }
         }

         else
         {
            ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);
            throw new Exception(string.Format(CultureInfo.InvariantCulture, "{0} is not an NTFS volume.", _driveInfo.Name));
         }

         ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);
      }




      /// <summary>
      /// CreateUsnJournal() creates a USN journal on the volume. If a journal already exists this function 
      /// will adjust the MaximumSize and AllocationDelta parameters of the journal if the requested size
      /// is larger.
      /// </summary>
      /// <param name="maxSize">maximum size requested for the UsnJournal</param>
      /// <param name="allocationDelta">when space runs out, the amount of additional
      /// space to allocate</param>
      /// <param name="elapsedTime">The TimeSpan object indicating how much time this function 
      /// took</param>
      /// <returns>a UsnJournalReturnCode
      /// USN_JOURNAL_SUCCESS                 CreateUsnJournal() function succeeded. 
      /// VOLUME_NOT_NTFS                     volume is not an NTFS volume.
      /// INVALID_HANDLE_VALUE                NtfsUsnJournal object failed initialization.
      /// USN_JOURNAL_NOT_ACTIVE              USN journal is not active on volume.
      /// ERROR_ACCESS_DENIED                 accessing the USN journal requires admin rights, see remarks.
      /// ERROR_INVALID_FUNCTION              error generated by DeviceIoControl() call.
      /// ERROR_FILE_NOT_FOUND                error generated by DeviceIoControl() call.
      /// ERROR_PATH_NOT_FOUND                error generated by DeviceIoControl() call.
      /// ERROR_TOO_MANY_OPEN_FILES           error generated by DeviceIoControl() call.
      /// ERROR_INVALID_HANDLE                error generated by DeviceIoControl() call.
      /// ERROR_INVALID_DATA                  error generated by DeviceIoControl() call.
      /// ERROR_NOT_SUPPORTED                 error generated by DeviceIoControl() call.
      /// ERROR_INVALID_PARAMETER             error generated by DeviceIoControl() call.
      /// ERROR_JOURNAL_DELETE_IN_PROGRESS    USN journal delete is in progress.
      /// ERROR_INVALID_USER_BUFFER           error generated by DeviceIoControl() call.
      /// USN_JOURNAL_ERROR                   unspecified USN journal error.
      /// </returns>
      /// <remarks>
      /// If function returns ERROR_ACCESS_DENIED you need to run application as an Administrator.
      /// </remarks>
      public int CreateUsnJournal(ulong maxSize, ulong allocationDelta)
      {
         var lastError = (int) UsnJournalReturnCode.VOLUME_NOT_NTFS;

         var sw = new Stopwatch();
         sw.Start();

         if (bNtfsVolume)
         {
            if (_usnJournalRootHandle.ToInt64() != Win32Api.INVALID_HANDLE_VALUE)
            {
               lastError = (int) UsnJournalReturnCode.USN_JOURNAL_SUCCESS;
               uint cb;

               var cujd = new Win32Api.CREATE_USN_JOURNAL_DATA
               {
                  MaximumSize = maxSize,
                  AllocationDelta = allocationDelta
               };

               var sizeCujd = Marshal.SizeOf(cujd);
               var cujdBuffer = Marshal.AllocHGlobal(sizeCujd);
               Win32Api.ZeroMemory(cujdBuffer, sizeCujd);
               Marshal.StructureToPtr(cujd, cujdBuffer, true);

               var fOk = Win32Api.DeviceIoControl(
                  _usnJournalRootHandle,
                  Win32Api.FSCTL_CREATE_USN_JOURNAL,
                  cujdBuffer,
                  sizeCujd,
                  IntPtr.Zero,
                  0,
                  out cb,
                  IntPtr.Zero);
               if (!fOk)
               {
                  lastError = Marshal.GetLastWin32Error();
               }
               Marshal.FreeHGlobal(cujdBuffer);
            }
            
            else
               lastError = (int) UsnJournalReturnCode.INVALID_HANDLE_VALUE;
         }

         ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);

         return lastError;
      }


      /// <summary>
      /// DeleteUsnJournal() deletes a USN journal on the volume. If no USN journal exists, this
      /// function simply returns success.
      /// </summary>
      /// <param name="journalState">USN_JOURNAL_DATA object for this volume</param>
      /// <returns>a UsnJournalReturnCode
      /// USN_JOURNAL_SUCCESS                 DeleteUsnJournal() function succeeded. 
      /// VOLUME_NOT_NTFS                     volume is not an NTFS volume.
      /// INVALID_HANDLE_VALUE                NtfsUsnJournal object failed initialization.
      /// USN_JOURNAL_NOT_ACTIVE              USN journal is not active on volume.
      /// ERROR_ACCESS_DENIED                 accessing the USN journal requires admin rights, see remarks.
      /// ERROR_INVALID_FUNCTION              error generated by DeviceIoControl() call.
      /// ERROR_FILE_NOT_FOUND                error generated by DeviceIoControl() call.
      /// ERROR_PATH_NOT_FOUND                error generated by DeviceIoControl() call.
      /// ERROR_TOO_MANY_OPEN_FILES           error generated by DeviceIoControl() call.
      /// ERROR_INVALID_HANDLE                error generated by DeviceIoControl() call.
      /// ERROR_INVALID_DATA                  error generated by DeviceIoControl() call.
      /// ERROR_NOT_SUPPORTED                 error generated by DeviceIoControl() call.
      /// ERROR_INVALID_PARAMETER             error generated by DeviceIoControl() call.
      /// ERROR_JOURNAL_DELETE_IN_PROGRESS    USN journal delete is in progress.
      /// ERROR_INVALID_USER_BUFFER           error generated by DeviceIoControl() call.
      /// USN_JOURNAL_ERROR                   unspecified USN journal error.
      /// </returns>
      /// <remarks>
      /// If function returns ERROR_ACCESS_DENIED you need to run application as an Administrator.
      /// </remarks>
      public int DeleteUsnJournal(Win32Api.USN_JOURNAL_DATA_V0 journalState)
      {
         var lastError = (int) UsnJournalReturnCode.VOLUME_NOT_NTFS;

         var sw = new Stopwatch();
         sw.Start();

         if (bNtfsVolume)
         {
            if (_usnJournalRootHandle.ToInt64() != Win32Api.INVALID_HANDLE_VALUE)
            {
               uint cb;

               var dujd = new Win32Api.DELETE_USN_JOURNAL_DATA
               {
                  UsnJournalID = journalState.UsnJournalID,
                  DeleteFlags = (uint) Win32Api.UsnJournalDeleteFlags.USN_DELETE_FLAG_DELETE
               };

               var sizeDujd = Marshal.SizeOf(dujd);
               var dujdBuffer = Marshal.AllocHGlobal(sizeDujd);
               Win32Api.ZeroMemory(dujdBuffer, sizeDujd);
               Marshal.StructureToPtr(dujd, dujdBuffer, true);

               Win32Api.DeviceIoControl(_usnJournalRootHandle, Win32Api.FSCTL_DELETE_USN_JOURNAL, dujdBuffer, sizeDujd, IntPtr.Zero, 0, out cb, IntPtr.Zero);

               lastError = Marshal.GetLastWin32Error();

               Marshal.FreeHGlobal(dujdBuffer);
            }

            else
               lastError = (int) UsnJournalReturnCode.INVALID_HANDLE_VALUE;
         }

         ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);

         return lastError;
      }


      /// <summary>Gets the directory entries from the Master File Table on a volume.</summary>
      public int GetUsnDirectories(out List<Win32Api.UsnEntry> folders)
      {
         folders = EnumerateUsnEntries(true).ToList();

         // Takes some time.
         folders.Sort();

         return (int) UsnJournalReturnCode.USN_JOURNAL_SUCCESS;
      }


      /// <summary>Gets the file entries from the Master File Table on a volume.</summary>
      public int GetUsnFiles(string filter, out List<Win32Api.UsnEntry> files)
      {
         files = EnumerateUsnEntries(false, filter).ToList();

         // Takes some time.
         files.Sort();

         return (int) UsnJournalReturnCode.USN_JOURNAL_SUCCESS;
      }


      /// <summary>Returns an enumerable collection of <see cref="Win32Api.UsnEntry"/> entries that meet specified criteria.</summary>
      /// <param name="onlyFolders"></param>
      /// <param name="filter">The filter.</param>
      private IEnumerable<Win32Api.UsnEntry> EnumerateUsnEntries(bool? onlyFolders, string filter = null)
      {
         var usnState = new Win32Api.USN_JOURNAL_DATA_V0();

         if (QueryUsnJournal(ref usnState) != (int)UsnJournalReturnCode.USN_JOURNAL_SUCCESS)
            throw new Win32Exception("Failed to query the USN journal on the volume.");


         if (string.IsNullOrWhiteSpace(filter) || filter.Equals("*", StringComparison.Ordinal))
            filter = null;

         var fileTypes = null != filter ? filter.Split(' ', ',', ';') : null;


         // Set up MFT_ENUM_DATA_V0 structure.
         var mftData = new Win32Api.MFT_ENUM_DATA_V0
         {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = usnState.NextUsn
         };

         var mftDataSize = Marshal.SizeOf(mftData);
         var mftDataBuffer = Marshal.AllocHGlobal(mftDataSize);

         Win32Api.ZeroMemory(mftDataBuffer, mftDataSize);
         Marshal.StructureToPtr(mftData, mftDataBuffer, true);


         // Set up the data buffer which receives the USN_RECORD data.
         const int pDataSize = sizeof(ulong) + 10000;
         var pData = Marshal.AllocHGlobal(pDataSize);
         Win32Api.ZeroMemory(pData, pDataSize);

         
         uint outBytesReturned;

         // Gather up volume's directories.
         while (Win32Api.DeviceIoControl(_usnJournalRootHandle, Win32Api.FSCTL_ENUM_USN_DATA, mftDataBuffer, mftDataSize, pData, pDataSize, out outBytesReturned, IntPtr.Zero))
         {
            var pUsnRecord = new IntPtr(pData.ToInt64() + sizeof(long));

            // While there is at least one entry in the USN journal.
            while (outBytesReturned > 60)
            {
               var usnEntry = new Win32Api.UsnEntry(pUsnRecord);


               if (null == onlyFolders)
                  yield return usnEntry;

               else if (usnEntry.IsFolder)
               {
                  if ((bool) onlyFolders)
                     yield return usnEntry;
               }

               else if (!(bool) onlyFolders)
               {
                  if (null == filter)
                     yield return usnEntry;

                  else
                  {
                     var extension = Path.GetExtension(usnEntry.Name);

                     if (!string.IsNullOrEmpty(extension))
                        foreach (var fileType in fileTypes)
                        {
                           if (fileType.Contains("*"))
                           {
                              if (extension.IndexOf(fileType.Trim('*'), StringComparison.OrdinalIgnoreCase) >= 0)
                                 yield return usnEntry;
                           }

                           else if (extension.Equals("." + fileType.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
                              yield return usnEntry;
                        }
                  }
               }


               pUsnRecord = new IntPtr(pUsnRecord.ToInt64() + usnEntry.RecordLength);
               outBytesReturned -= usnEntry.RecordLength;
            }

            Marshal.WriteInt64(mftDataBuffer, Marshal.ReadInt64(pData, 0));
         }

         Marshal.FreeHGlobal(pData);
      }


      /// <summary>Given a file reference number GetPathFromFrn() calculates the full path in the out parameter 'path'.</summary>
      /// <param name="frn">A 64-bit file reference number</param>
      /// <param name="path"></param>
      /// <returns>
      /// USN_JOURNAL_SUCCESS                 GetPathFromFrn() function succeeded. 
      /// VOLUME_NOT_NTFS                     volume is not an NTFS volume.
      /// INVALID_HANDLE_VALUE                NtfsUsnJournal object failed initialization.
      /// ERROR_ACCESS_DENIED                 accessing the USN journal requires admin rights, see remarks.
      /// INVALID_FILE_REFERENCE_NUMBER       file reference number not found in Master File Table.
      /// ERROR_INVALID_FUNCTION              error generated by NtCreateFile() or NtQueryInformationFile() call.
      /// ERROR_FILE_NOT_FOUND                error generated by NtCreateFile() or NtQueryInformationFile() call.
      /// ERROR_PATH_NOT_FOUND                error generated by NtCreateFile() or NtQueryInformationFile() call.
      /// ERROR_TOO_MANY_OPEN_FILES           error generated by NtCreateFile() or NtQueryInformationFile() call.
      /// ERROR_INVALID_HANDLE                error generated by NtCreateFile() or NtQueryInformationFile() call.
      /// ERROR_INVALID_DATA                  error generated by NtCreateFile() or NtQueryInformationFile() call.
      /// ERROR_NOT_SUPPORTED                 error generated by NtCreateFile() or NtQueryInformationFile() call.
      /// ERROR_INVALID_PARAMETER             error generated by NtCreateFile() or NtQueryInformationFile() call.
      /// ERROR_INVALID_USER_BUFFER           error generated by NtCreateFile() or NtQueryInformationFile() call.
      /// USN_JOURNAL_ERROR                   unspecified USN journal error.
      /// </returns>
      /// <remarks>
      /// If function returns ERROR_ACCESS_DENIED you need to run application as an Administrator.
      /// </remarks>
      public int GetPathFromFileReference(ulong frn, out string path)
      {
         path = null;
         var lastError = (int) UsnJournalReturnCode.VOLUME_NOT_NTFS;
         
         var sw = new Stopwatch();
         sw.Start();

         if (bNtfsVolume)
         {
            if (_usnJournalRootHandle.ToInt64() != Win32Api.INVALID_HANDLE_VALUE)
            {
               if (frn != 0)
               {
                  lastError = (int) UsnJournalReturnCode.USN_JOURNAL_SUCCESS;

                  long allocSize = 0;
                  Win32Api.UNICODE_STRING unicodeString;
                  var objAttributes = new Win32Api.OBJECT_ATTRIBUTES();
                  var ioStatusBlock = new Win32Api.IO_STATUS_BLOCK();
                  var hFile = IntPtr.Zero;

                  var buffer = Marshal.AllocHGlobal(4096);
                  var refPtr = Marshal.AllocHGlobal(8);
                  var objAttIntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(objAttributes));

                  // Pointer >> fileid.
                  Marshal.WriteInt64(refPtr, (long) frn);

                  unicodeString.Length = 8;
                  unicodeString.MaximumLength = 8;
                  unicodeString.Buffer = refPtr;

                  // Copy unicode structure to pointer.
                  Marshal.StructureToPtr(unicodeString, objAttIntPtr, true);


                  //  InitializeObjectAttributes.
                  objAttributes.Length = (ulong) Marshal.SizeOf(objAttributes);
                  objAttributes.ObjectName = objAttIntPtr;
                  objAttributes.RootDirectory = _usnJournalRootHandle;
                  objAttributes.Attributes = (int) Win32Api.OBJ_CASE_INSENSITIVE;


                  var fOk = Win32Api.NtCreateFile(ref hFile, FileAccess.Read, ref objAttributes, ref ioStatusBlock, ref allocSize, 0,
                     FileShare.ReadWrite, Win32Api.FILE_OPEN, Win32Api.FILE_OPEN_BY_FILE_ID | Win32Api.FILE_OPEN_FOR_BACKUP_INTENT, IntPtr.Zero, 0);


                  if (fOk == 0)
                  {
                     fOk = Win32Api.NtQueryInformationFile(hFile, ref ioStatusBlock, buffer, 4096, Win32Api.FILE_INFORMATION_CLASS.FileNameInformation);

                     if (fOk == 0)
                     {
                        // The first 4 bytes are the name length.
                        var nameLength = Marshal.ReadInt32(buffer, 0);

                        // The next bytes are the name.
                        path = Marshal.PtrToStringUni(new IntPtr(buffer.ToInt64() + 4), nameLength / 2);
                     }
                  }

                  Win32Api.CloseHandle(hFile);
                  Marshal.FreeHGlobal(buffer);
                  Marshal.FreeHGlobal(objAttIntPtr);
                  Marshal.FreeHGlobal(refPtr);
               }
            }
         }

         ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);

         return lastError;
      }


      /// <summary>GetUsnJournalState() gets the current state of the USN journal if it is active.</summary>
      /// <param name="usnJournalState">
      /// Reference to USN journal data object filled with the current USN journal state.
      /// </param>
      /// <param name="elapsedTime">The elapsed time for the GetUsnJournalState() function call.</param>
      /// <returns>
      /// USN_JOURNAL_SUCCESS                 GetUsnJournalState() function succeeded. 
      /// VOLUME_NOT_NTFS                     volume is not an NTFS volume.
      /// INVALID_HANDLE_VALUE                NtfsUsnJournal object failed initialization.
      /// USN_JOURNAL_NOT_ACTIVE              USN journal is not active on volume.
      /// ERROR_ACCESS_DENIED                 accessing the USN journal requires admin rights, see remarks.
      /// ERROR_INVALID_FUNCTION              error generated by DeviceIoControl() call.
      /// ERROR_FILE_NOT_FOUND                error generated by DeviceIoControl() call.
      /// ERROR_PATH_NOT_FOUND                error generated by DeviceIoControl() call.
      /// ERROR_TOO_MANY_OPEN_FILES           error generated by DeviceIoControl() call.
      /// ERROR_INVALID_HANDLE                error generated by DeviceIoControl() call.
      /// ERROR_INVALID_DATA                  error generated by DeviceIoControl() call.
      /// ERROR_NOT_SUPPORTED                 error generated by DeviceIoControl() call.
      /// ERROR_INVALID_PARAMETER             error generated by DeviceIoControl() call.
      /// ERROR_JOURNAL_DELETE_IN_PROGRESS    USN journal delete is in progress.
      /// ERROR_INVALID_USER_BUFFER           error generated by DeviceIoControl() call.
      /// USN_JOURNAL_ERROR                   unspecified USN journal error.
      /// </returns>
      /// <remarks>
      /// If function returns ERROR_ACCESS_DENIED you need to run application as an Administrator.
      /// </remarks>
      public int GetUsnJournalState(ref Win32Api.USN_JOURNAL_DATA_V0 usnJournalState)
      {
         var lastError = (int) UsnJournalReturnCode.VOLUME_NOT_NTFS;

         var sw = new Stopwatch();
         sw.Start();

         if (bNtfsVolume)
         {
            lastError = _usnJournalRootHandle.ToInt64() == Win32Api.INVALID_HANDLE_VALUE
               ? (int) UsnJournalReturnCode.INVALID_HANDLE_VALUE
               : QueryUsnJournal(ref usnJournalState);
         }

         ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);

         return lastError;
      }


      /// <summary>Given a previous state, GetUsnJournalEntries() determines if the USN journal is active and
      /// no USN journal entries have been lost (i.e. USN journal is valid), then
      /// it loads a SortedList<UInt64, Win32Api.UsnEntry> list and returns it as the out parameter 'usnEntries'.
      /// If GetUsnJournalChanges returns anything but USN_JOURNAL_SUCCESS, the usnEntries list will 
      /// be empty.
      /// </summary>
      /// <param name="previousUsnState">The USN journal state the last time volume changes were requested.</param>
      /// <param name="reasonMask"></param>
      /// <param name="usnEntries"></param>
      /// <param name="newUsnState"></param>
      /// <returns>
      /// USN_JOURNAL_SUCCESS                 GetUsnJournalChanges() function succeeded. 
      /// VOLUME_NOT_NTFS                     volume is not an NTFS volume.
      /// INVALID_HANDLE_VALUE                NtfsUsnJournal object failed initialization.
      /// USN_JOURNAL_NOT_ACTIVE              USN journal is not active on volume.
      /// ERROR_ACCESS_DENIED                 accessing the USN journal requires admin rights, see remarks.
      /// ERROR_INVALID_FUNCTION              error generated by DeviceIoControl() call.
      /// ERROR_FILE_NOT_FOUND                error generated by DeviceIoControl() call.
      /// ERROR_PATH_NOT_FOUND                error generated by DeviceIoControl() call.
      /// ERROR_TOO_MANY_OPEN_FILES           error generated by DeviceIoControl() call.
      /// ERROR_INVALID_HANDLE                error generated by DeviceIoControl() call.
      /// ERROR_INVALID_DATA                  error generated by DeviceIoControl() call.
      /// ERROR_NOT_SUPPORTED                 error generated by DeviceIoControl() call.
      /// ERROR_INVALID_PARAMETER             error generated by DeviceIoControl() call.
      /// ERROR_JOURNAL_DELETE_IN_PROGRESS    USN journal delete is in progress.
      /// ERROR_INVALID_USER_BUFFER           error generated by DeviceIoControl() call.
      /// USN_JOURNAL_ERROR                   unspecified USN journal error.
      /// </returns>
      /// <remarks>
      /// If function returns ERROR_ACCESS_DENIED you need to run application as an Administrator.
      /// </remarks>
      public int GetUsnJournalEntries(Win32Api.USN_JOURNAL_DATA_V0 previousUsnState, uint reasonMask, out List<Win32Api.UsnEntry> usnEntries, out Win32Api.USN_JOURNAL_DATA_V0 newUsnState)
      {
        usnEntries = new List<Win32Api.UsnEntry>();
         newUsnState = new Win32Api.USN_JOURNAL_DATA_V0();
         var lastError = (int) UsnJournalReturnCode.VOLUME_NOT_NTFS;

         var sw = new Stopwatch();
         sw.Start();

         if (bNtfsVolume)
         {
            if (_usnJournalRootHandle.ToInt64() != Win32Api.INVALID_HANDLE_VALUE)
            {
               // Get current USN journal state.
               lastError = QueryUsnJournal(ref newUsnState);
               if (lastError == (int) UsnJournalReturnCode.USN_JOURNAL_SUCCESS)
               {
                  var bReadMore = true;

                  // Sequentially process the USN journal looking for image file entries.
                  const int pbDataSize = sizeof(ulong) * 16384;
                  var pbData = Marshal.AllocHGlobal(pbDataSize);
                  Win32Api.ZeroMemory(pbData, pbDataSize);

                  var rujd = new Win32Api.READ_USN_JOURNAL_DATA_V0
                  {
                     StartUsn = (ulong) previousUsnState.NextUsn,
                     ReasonMask = reasonMask,
                     ReturnOnlyOnClose = 0,
                     Timeout = 0,
                     BytesToWaitFor = 0,
                     UsnJournalId = previousUsnState.UsnJournalID
                  };

                  var sizeRujd = Marshal.SizeOf(rujd);
                  var rujdBuffer = Marshal.AllocHGlobal(sizeRujd);
                  Win32Api.ZeroMemory(rujdBuffer, sizeRujd);
                  Marshal.StructureToPtr(rujd, rujdBuffer, true);

                  // Read USN journal entries.
                  while (bReadMore)
                  {
                     uint outBytesReturned;

                     var bRtn = Win32Api.DeviceIoControl(_usnJournalRootHandle, Win32Api.FSCTL_READ_USN_JOURNAL, rujdBuffer, sizeRujd, pbData, pbDataSize, out outBytesReturned, IntPtr.Zero);
                     if (bRtn)
                     {
                        var pUsnRecord = new IntPtr(pbData.ToInt64() + sizeof(ulong));

                        // While there is at least one entry in the USN journal.
                        while (outBytesReturned > 60) 
                        {
                           var usnEntry = new Win32Api.UsnEntry(pUsnRecord);

                           // Only read until the current usn points beyond the current state's USN.
                           if (usnEntry.USN >= newUsnState.NextUsn)
                           {
                              bReadMore = false;
                              break;
                           }

                           usnEntries.Add(usnEntry);

                           pUsnRecord = new IntPtr(pUsnRecord.ToInt64() + usnEntry.RecordLength);
                           outBytesReturned -= usnEntry.RecordLength;
                        }
                     }

                     else
                     {
                        var lastWin32Error = Marshal.GetLastWin32Error();
                        if (lastWin32Error == (int) Win32Api.GetLastErrorEnum.ERROR_HANDLE_EOF)
                           lastError = (int) UsnJournalReturnCode.USN_JOURNAL_SUCCESS;

                        break;
                     }

                     var nextUsn = Marshal.ReadInt64(pbData, 0);
                     if (nextUsn >= newUsnState.NextUsn)
                        break;

                     Marshal.WriteInt64(rujdBuffer, nextUsn);
                  }

                  Marshal.FreeHGlobal(rujdBuffer);
                  Marshal.FreeHGlobal(pbData);
               }
            }

            else
               lastError = (int) UsnJournalReturnCode.INVALID_HANDLE_VALUE;
         }

         ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);

         return lastError;
      }


      /// <summary>Tests to see if the USN journal is active on the volume.</summary>
      /// <returns>true if USN journal is active, false if no USN journal on volume</returns>
      public bool IsUsnJournalActive()
      {
         var success = false;

         var sw = new Stopwatch();
         sw.Start();

         if (bNtfsVolume)
         {
            if (_usnJournalRootHandle.ToInt64() != Win32Api.INVALID_HANDLE_VALUE)
            {
               var usnJournalCurrentState = new Win32Api.USN_JOURNAL_DATA_V0();
               var lastError = QueryUsnJournal(ref usnJournalCurrentState);

               if (lastError == (int) UsnJournalReturnCode.USN_JOURNAL_SUCCESS)
                  success = true;
            }
         }

         ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);

         return success;
      }


      /// <summary>Tests to see if there is a USN journal on this volume and if there is determines whether any journal entries have been lost.</summary>
      /// <returns>true if the USN journal is active and if the JournalId's are the same 
      /// and if all the USN journal entries expected by the previous state are available 
      /// from the current state. false if not.
      /// </returns>
      public bool IsUsnJournalValid(Win32Api.USN_JOURNAL_DATA_V0 usnJournalPreviousState)
      {
         var usnJournalState = new Win32Api.USN_JOURNAL_DATA_V0();

         var sw = new Stopwatch();
         sw.Start();

         var success = bNtfsVolume && _usnJournalRootHandle.ToInt64() != Win32Api.INVALID_HANDLE_VALUE &&
                QueryUsnJournal(ref usnJournalState) == (int) UsnJournalReturnCode.USN_JOURNAL_SUCCESS &&
                usnJournalPreviousState.UsnJournalID == usnJournalState.UsnJournalID &&
                usnJournalPreviousState.NextUsn >= usnJournalState.NextUsn;

         ElapsedTime = TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds);

         return success;
      }


      /// <summary>Gets a Volume Serial Number for the volume represented by driveInfo.</summary>
      /// <param name="driveInfo">DriveInfo object representing the volume in question.</param>
      /// <param name="volumeSerialNumber">out parameter to hold the volume serial number.</param>
      /// <returns></returns>
      private static int GetVolumeSerialNumber(DriveInfo driveInfo, out uint volumeSerialNumber)
      {
         Console.WriteLine("GetVolumeSerialNumber function entered for drive: {0}", driveInfo.Name);

         volumeSerialNumber = 0;
         var lastError = 0;
         var pathRoot = string.Concat(@"\\.\", driveInfo.Name);

         var hRoot = Win32Api.CreateFile(pathRoot,
            0,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Win32Api.OPEN_EXISTING,
            Win32Api.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

         if (hRoot.ToInt64() != Win32Api.INVALID_HANDLE_VALUE)
         {
            Win32Api.BY_HANDLE_FILE_INFORMATION fi;
            var bRtn = Win32Api.GetFileInformationByHandle(hRoot, out fi);
            lastError = Marshal.GetLastWin32Error();

            if (bRtn)
               volumeSerialNumber = fi.VolumeSerialNumber;

            Win32Api.CloseHandle(hRoot);
         }

         else
            lastError = Marshal.GetLastWin32Error();

         return lastError;
      }


      private int GetRootHandle(out IntPtr rootHandle)
      {
         var lastError = 0;

         var vol = string.Concat(@"\\.\", _driveInfo.Name.TrimEnd('\\'));
         
         rootHandle = Win32Api.CreateFile(vol, Win32Api.GENERIC_READ | Win32Api.GENERIC_WRITE, Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE, IntPtr.Zero, Win32Api.OPEN_EXISTING, 0, IntPtr.Zero);

         if (rootHandle.ToInt64() == Win32Api.INVALID_HANDLE_VALUE)
            lastError = Marshal.GetLastWin32Error();

         return lastError;
      }


      /// <summary>This function queries the USN journal on the volume.</summary>
      /// <param name="usnJournalState">the USN_JOURNAL_DATA object that is associated with this volume</param>
      private int QueryUsnJournal(ref Win32Api.USN_JOURNAL_DATA_V0 usnJournalState)
      {
         uint cb;

         Win32Api.DeviceIoControl(_usnJournalRootHandle, Win32Api.FSCTL_QUERY_USN_JOURNAL, IntPtr.Zero, 0, out usnJournalState, Marshal.SizeOf(usnJournalState), out cb, IntPtr.Zero);

         return Marshal.GetLastWin32Error();
      }
      

      #region Disposable Members

      ~NtfsUsnJournal()
      {
         Dispose(false);
      }

      public void Dispose()
      {
         Dispose(true);
         GC.SuppressFinalize(this);
      }

      private void Dispose(bool disposing)
      {
         if (disposing)
            if (_usnJournalRootHandle != IntPtr.Zero)
            {
               Win32Api.CloseHandle(_usnJournalRootHandle);
               _usnJournalRootHandle = IntPtr.Zero;
            }
      }

      #endregion // Disposable Members
   }
}
