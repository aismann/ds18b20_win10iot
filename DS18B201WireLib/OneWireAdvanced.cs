﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;

namespace DS18B201WireLib
{
    public class OneWireAdvanced
    {
        public String DeviceId { get; set; }

        // global search state
        private byte[] ROM_NO = new byte[8];
        private int LastDiscrepancy;
        private int LastFamilyDiscrepancy;
        private bool LastDeviceFlag;
        private byte crc8;

        static byte[] dscrc_table = {
                        0, 94,188,226, 97, 63,221,131,194,156,126, 32,163,253, 31, 65,
                      157,195, 33,127,252,162, 64, 30, 95,  1,227,189, 62, 96,130,220,
                       35,125,159,193, 66, 28,254,160,225,191, 93,  3,128,222, 60, 98,
                      190,224,  2, 92,223,129, 99, 61,124, 34,192,158, 29, 67,161,255,
                       70, 24,250,164, 39,121,155,197,132,218, 56,102,229,187, 89,  7,
                      219,133,103, 57,186,228,  6, 88, 25, 71,165,251,120, 38,196,154,
                      101, 59,217,135,  4, 90,184,230,167,249, 27, 69,198,152,122, 36,
                      248,166, 68, 26,153,199, 37,123, 58,100,134,216, 91,  5,231,185,
                      140,210, 48,110,237,179, 81, 15, 78, 16,242,172, 47,113,147,205,
                       17, 79,173,243,112, 46,204,146,211,141,111, 49,178,236, 14, 80,
                      175,241, 19, 77,206,144,114, 44,109, 51,209,143, 12, 82,176,238,
                       50,108,142,208, 83, 13,239,177,240,174, 76, 18,145,207, 45,115,
                      202,148,118, 40,171,245, 23, 73,  8, 86,180,234,105, 55,213,139,
                       87,  9,235,181, 54,104,138,212,149,203, 41,119,244,170, 72, 22,
                      233,183, 85, 11,136,214, 52,106, 43,117,151,201, 74, 20,246,168,
                      116, 42,200,150, 21, 75,169,247,182,232, 10, 84,215,137,107, 53};

        private SerialDevice serialPort = null;
        DataWriter dataWriteObject = null;
        DataReader dataReaderObject = null;

        public void shutdown()
        {
            if (serialPort != null)
            {
                serialPort.Dispose();
                serialPort = null;
            }
        }

        async Task<bool> reset()
        {
            try
            {
                if (serialPort != null)
                    serialPort.Dispose();

                serialPort = await SerialDevice.FromIdAsync(DeviceId);

                // Configure serial settings
                serialPort.WriteTimeout = TimeSpan.FromMilliseconds(1000);
                serialPort.ReadTimeout = TimeSpan.FromMilliseconds(1000);
                serialPort.BaudRate = 9600;
                serialPort.Parity = SerialParity.None;
                serialPort.StopBits = SerialStopBitCount.One;
                serialPort.DataBits = 8;
                serialPort.Handshake = SerialHandshake.None;

                dataWriteObject = new DataWriter(serialPort.OutputStream);
                dataWriteObject.WriteByte(0xF0);
                await dataWriteObject.StoreAsync();

                dataReaderObject = new DataReader(serialPort.InputStream);
                await dataReaderObject.LoadAsync(1);
                byte resp = dataReaderObject.ReadByte();
                if (resp == 0xFF)
                {
                    System.Diagnostics.Debug.WriteLine("Nothing connected to UART");
                    return false;
                }
                else if (resp == 0xF0)
                {
                    System.Diagnostics.Debug.WriteLine("No 1-wire devices are present");
                    return false;
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine("Response: " + resp);
                    serialPort.Dispose();
                    serialPort = await SerialDevice.FromIdAsync(DeviceId);

                    // Configure serial settings
                    serialPort.WriteTimeout = TimeSpan.FromMilliseconds(1000);
                    serialPort.ReadTimeout = TimeSpan.FromMilliseconds(1000);
                    serialPort.BaudRate = 115200;
                    serialPort.Parity = SerialParity.None;
                    serialPort.StopBits = SerialStopBitCount.One;
                    serialPort.DataBits = 8;
                    serialPort.Handshake = SerialHandshake.None;
                    dataWriteObject = new DataWriter(serialPort.OutputStream);
                    dataReaderObject = new DataReader(serialPort.InputStream);
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Exception: " + ex.Message);
                return false;
            }
        }

        public async Task<double> getTemperature()
        {
            double tempCelsius = -200;

            if (await reset())
            {
                await writeByte(0xCC); //1-Wire SKIP ROM command (ignore device id)
                await writeByte(0x44); //DS18B20 convert T command 
                                              // (initiate single temperature conversion)
                                              // thermal data is stored in 2-byte temperature 
                                              // register in scratchpad memory

                // Wait for at least 750ms for data to be collated
                await Task.Delay(750);

                // Get the data
                await reset();
                await writeByte(0xCC); //1-Wire Skip ROM command (ignore device id)
                await writeByte(0xBE); //DS18B20 read scratchpad command
                                              // DS18B20 will transmit 9 bytes to master (us)
                                              // starting with the LSB

                byte tempLSB = await readByte(); //read lsb
                byte tempMSB = await readByte(); //read msb

                // Reset bus to stop sensor sending unwanted data
                await reset();

                // Log the Celsius temperature
                tempCelsius = ((tempMSB * 256) + tempLSB) / 16.0;
                var temp2 = ((tempMSB << 8) + tempLSB) * 0.0625; //just another way of calculating it

                System.Diagnostics.Debug.WriteLine("Temperature: " + tempCelsius + " degrees C " + temp2);
            }
            return tempCelsius;
        }

        public async Task<double> getTemperature(byte[] rom_id)
        {
            double tempCelsius = -200;

            if (await reset())
            {
                await writeByte(0x55); //match rom
                for (int i = 0; i < 8; i++) //Writes the 64bit ID of the oneWireDevice
                {
                    await writeByte((byte)rom_id[i]);
                }
                await writeByte(0x44); //DS18B20 convert T command 
                                       // (initiate single temperature conversion)
                                       // thermal data is stored in 2-byte temperature 
                                       // register in scratchpad memory

                // Wait for at least 750ms for data to be collated
                await Task.Delay(750);

                // Get the data
                await reset();
                await writeByte(0x55); //1-Wire Skip ROM command (ignore device id)
                for (int i = 0; i < 8; i++) //Writes the 64bit ID of the oneWireDevice
                {
                    await writeByte((byte)rom_id[i]);
                }
                await writeByte(0xBE); //DS18B20 read scratchpad command
                                       // DS18B20 will transmit 9 bytes to master (us)
                                       // starting with the LSB

                byte tempLSB = await readByte(); //read lsb
                byte tempMSB = await readByte(); //read msb

                // Reset bus to stop sensor sending unwanted data
                await reset();

                // Log the Celsius temperature
                tempCelsius = ((tempMSB * 256) + tempLSB) / 16.0;
                var temp2 = ((tempMSB << 8) + tempLSB) * 0.0625; //just another way of calculating it

                System.Diagnostics.Debug.WriteLine("Temperature: " + tempCelsius + " degrees C " + temp2);
            }
            return tempCelsius;
        }

        public async Task writeByte(byte b)
        {
            for (byte i = 0; i < 8; i++, b = (byte)(b >> 1))
            {
                // Run through the bits in the byte, extracting the
                // LSB (bit 0) and sending it to the bus
                await writeBit((byte)(b & 0x01));
            }
        }

        async Task<byte> writeBit(byte b)
        {
            var bit = b > 0 ? 0xFF : 0x00;
            dataWriteObject.WriteByte((byte)bit);
            await dataWriteObject.StoreAsync();
            await dataReaderObject.LoadAsync(1);
            var data = dataReaderObject.ReadByte();
            return (byte)(data & 0xFF);
        }

        async Task<byte> readBit()
        {
            byte b = 0;
            for (byte i = 0; i < 1; i++)
            {
                // Build up byte bit by bit, LSB first
                b = (byte)((b >> 1) + 0x80 * await writeBit(1));
            }
            //System.Diagnostics.Debug.WriteLine("onewireReadBit result: " + b);
            return b;
        }

        async Task<byte> readByte()
        {
            byte b = 0;
            for (byte i = 0; i < 8; i++)
            {
                // Build up byte bit by bit, LSB first
                b = (byte)((b >> 1) + 0x80 * await writeBit(1));
            }
            //System.Diagnostics.Debug.WriteLine("onewireReadByte result: " + b);
            return b;
        }

        public async Task<List<byte[]>> discover()
        {
            List<byte[]> devices = new List<byte[]>();
            bool result = await first();
            while (result)
            {
                devices.Add(ROM_NO.ToArray());
                result = await next();
            }
            return devices;
        }

        //--------------------------------------------------------------------------
        // Find the 'first' devices on the 1-Wire bus
        // Return TRUE  : device found, ROM number in ROM_NO buffer
        //        FALSE : no device present
        //
        async Task<bool> first()
        {
            // reset the search state
            LastDiscrepancy = 0;
            LastDeviceFlag = false;
            LastFamilyDiscrepancy = 0;

            return await search();
        }

        //--------------------------------------------------------------------------
        // Find the 'next' devices on the 1-Wire bus
        // Return TRUE  : device found, ROM number in ROM_NO buffer
        //        FALSE : device not found, end of search
        //
        async Task<bool> next()
        {
            // leave the search state alone
            return await search();
        }

        //--------------------------------------------------------------------------
        // Perform the 1-Wire Search Algorithm on the 1-Wire bus using the existing
        // search state.
        // Return TRUE  : device found, ROM number in ROM_NO buffer
        //        FALSE : device not found, end of search
        //
        async Task<bool> search()
        {
            bool search_result = false;
            int id_bit_number;
            int last_zero, rom_byte_number;
            int id_bit, cmp_id_bit;
            byte rom_byte_mask;
            bool search_direction;

            // initialize for search
            id_bit_number = 1;
            last_zero = 0;
            rom_byte_number = 0;
            rom_byte_mask = 1;
            search_result = false;
            crc8 = 0;

            // if the last call was not the last one
            if (!LastDeviceFlag)
            {
                // 1-Wire reset
                if (!await reset())
                {
                    // reset the search
                    LastDiscrepancy = 0;
                    LastDeviceFlag = false;
                    LastFamilyDiscrepancy = 0;
                    return false;
                }

                // issue the search command 
                await writeByte(0xF0);

                // loop to do the search
                do
                {
                    // read a bit and its complement
                    id_bit = await readBit();
                    cmp_id_bit = await readBit();

                    // check for no devices on 1-wire
                    if ((id_bit == 1) && (cmp_id_bit == 1))
                        break;
                    else
                    {
                        // all devices coupled have 0 or 1
                        if (id_bit != cmp_id_bit)
                            search_direction = id_bit > 1;  // bit write value for search
                        else
                        {
                            // if this discrepancy if before the Last Discrepancy
                            // on a previous next then pick the same as last time
                            if (id_bit_number < LastDiscrepancy)
                                search_direction = ((ROM_NO[rom_byte_number] & rom_byte_mask) > 0);
                            else
                                // if equal to last pick 1, if not then pick 0
                                search_direction = (id_bit_number == LastDiscrepancy);

                            // if 0 was picked then record its position in LastZero
                            if (search_direction == false)
                            {
                                last_zero = id_bit_number;

                                // check for Last discrepancy in family
                                if (last_zero < 9)
                                    LastFamilyDiscrepancy = last_zero;
                            }
                        }

                        // set or clear the bit in the ROM byte rom_byte_number
                        // with mask rom_byte_mask
                        if (search_direction)
                            ROM_NO[rom_byte_number] |= rom_byte_mask;
                        else
                            ROM_NO[rom_byte_number] &= (byte)~rom_byte_mask;

                        // serial number search direction write bit
                        await writeBit(Convert.ToByte(search_direction));

                        // increment the byte counter id_bit_number
                        // and shift the mask rom_byte_mask
                        id_bit_number++;
                        rom_byte_mask <<= 1;

                        // if the mask is 0 then go to new SerialNum byte rom_byte_number and reset mask
                        if (rom_byte_mask == 0)
                        {
                            docrc8(ROM_NO[rom_byte_number]);  // accumulate the CRC
                            rom_byte_number++;
                            rom_byte_mask = 1;
                        }
                    }
                }
                while (rom_byte_number < 8);  // loop until through all ROM bytes 0-7

                // if the search was successful then
                if (!((id_bit_number < 65) || (crc8 != 0)))
                {
                    // search successful so set LastDiscrepancy,LastDeviceFlag,search_result
                    LastDiscrepancy = last_zero;

                    // check for last device
                    if (LastDiscrepancy == 0)
                        LastDeviceFlag = true;

                    search_result = true;
                }
            }

            // if no device found then reset counters so next 'search' will be like a first
            if (!search_result || ROM_NO[0] == 0)
            {
                //System.Diagnostics.Debug.WriteLine("NUFFIN!");
                LastDiscrepancy = 0;
                LastDeviceFlag = false;
                LastFamilyDiscrepancy = 0;
                search_result = false;
            }
            /*
            else
            {
                System.Diagnostics.Debug.WriteLine("WIRE FOUND!");
                for (int i = 7; i >= 0; i--)
                    System.Diagnostics.Debug.Write(String.Format("{0:X2}", ROM_NO[i]));
            }
            */

            return search_result;
        }

        //--------------------------------------------------------------------------
        // Verify the device with the ROM number in ROM_NO buffer is present.
        // Return TRUE  : device verified present
        //        FALSE : device not present
        //
        async Task<bool> verify()
        {
            byte[] rom_backup = new byte[8];
            bool rslt = false;
            bool ldf_backup = false;
            int i, ld_backup, lfd_backup;

            // keep a backup copy of the current state
            for (i = 0; i < 8; i++)
                rom_backup[i] = ROM_NO[i];
            ld_backup = LastDiscrepancy;
            ldf_backup = LastDeviceFlag;
            lfd_backup = LastFamilyDiscrepancy;

            // set search to find the same device
            LastDiscrepancy = 64;
            LastDeviceFlag = false;

            if (await search())
            {
                // check if same device found
                rslt = true;
                for (i = 0; i < 8; i++)
                {
                    if (rom_backup[i] != ROM_NO[i])
                    {
                        rslt = false;
                        break;
                    }
                }
            }
            else
                rslt = false;

            // restore the search state 
            for (i = 0; i < 8; i++)
                ROM_NO[i] = rom_backup[i];
            LastDiscrepancy = ld_backup;
            LastDeviceFlag = ldf_backup;
            LastFamilyDiscrepancy = lfd_backup;

            // return the result of the verify
            return rslt;
        }

        //--------------------------------------------------------------------------
        // Setup the search to find the device type 'family_code' on the next call
        // to OWNext() if it is present.
        //
        void targetSetup(byte family_code)
        {
            int i;

            // set the search state to find SearchFamily type devices
            ROM_NO[0] = family_code;
            for (i = 1; i < 8; i++)
                ROM_NO[i] = 0;
            LastDiscrepancy = 64;
            LastFamilyDiscrepancy = 0;
            LastDeviceFlag = false;
        }

        //--------------------------------------------------------------------------
        // Setup the search to skip the current device type on the next call
        // to OWNext().
        //
        void familySkipSetup()
        {
            // set the Last discrepancy to last family discrepancy
            LastDiscrepancy = LastFamilyDiscrepancy;
            LastFamilyDiscrepancy = 0;

            // check for end of list
            if (LastDiscrepancy == 0)
                LastDeviceFlag = true;
        }
        //--------------------------------------------------------------------------
        // Calculate the CRC8 of the byte value provided with the current 
        // global 'crc8' value. 
        // Returns current global crc8 value
        //
        byte docrc8(byte value)
        {
            // See Application Note 27

            // TEST BUILD
            crc8 = dscrc_table[crc8 ^ value];
            return crc8;
        }
    }
}
