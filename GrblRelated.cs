/*  GRBL-Plotter. Another GCode sender for GRBL.
    This file is part of the GRBL-Plotter application.
   
    Copyright (C) 2015-2023 Sven Hasemann contact: svenhb@web.de

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
/*
 * 2016-12-31 Add GRBL 1.1 information
 * 2018-04-07 reorder
 * 2018-01-01 edit parseStatus to identify also Hold:0
 * 2019-05-10 move _serial_form.isGrblVers0 to here grbl.isVersion_0
 * https://github.com/fra589/grbl-Mega-5X
 * 2019-08-13 add PRB, TLO status
 * 2020-01-04 add "errorBecauseOfBadCode"
 * 2020-01-13 localization of grblStatus (Idle, run, hold...)
 * 2020-08-08 #145
 * 2021-01-16 StreamEventArgs : EventArgs -> switch from float to int for codeFinish, buffFinish %
 * 2021-05-01 return last index of splitted error, to catch "error: Invalid gcode ID:24" line 417
 * 2021-07-26 code clean up / code quality
 * 2021-09-29 add Status
 * 2021-11-03 support VoidMicro controller: https://github.com/arkypita/LaserGRBL/issues/1640
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace GrblPlotter
{
    internal static class Grbl
    {       // need to have global access to this data?
        internal static XyzPoint posWCO = new XyzPoint(0, 0, 0);
        internal static XyzPoint posWork = new XyzPoint(0, 0, 0);
        internal static XyzPoint posMachine = new XyzPoint(0, 0, 0);
        internal static GrblState Status = GrblState.unknown;

        public static bool posChanged = true;
        public static bool wcoChanged = true;

        public static bool isVersion_0 = true;  // note if grbl version <=0.9 or >=1.1
        public static bool isMarlin = false;
        public static bool isConnected = false;

        public static int axisCount = 0;
        public static bool axisA = false;       // axis A available?
        public static bool axisB = false;       // axis B available?
        public static bool axisC = false;       // axis C available?
        public static bool axisUpdate = false;  // update of GUI needed
        public static int RX_BUFFER_SIZE = 127; // grbl buffer size inside Arduino
        public static int pollInterval = 200;
        public static int bufferSize = -1;
        public static string lastMessage = "";
        public static short lastErrorNr = 0;
        public static int DefaultFeed = 1000;

        public static bool grblSimulate = false;
        private static readonly Dictionary<int, float> settings = new Dictionary<int, float>();    // keep $$-settings
        private static readonly Dictionary<string, XyzPoint> coordinates = new Dictionary<string, XyzPoint>();    // keep []-settings
        private static readonly Dictionary<string, string> messages = new Dictionary<string, string>();

        private static XyzPoint _posMarker = new XyzPoint(0, 0, 0);
        private static double _posMarkerAngle = 0;
        //      private static XyzPoint _posMarkerOld = new XyzPoint(0, 0, 0);
        internal static XyzPoint PosMarker
        {
            get
            { return _posMarker; }
            set
            { _posMarker = value; }
        }
        public static double PosMarkerAngle
        {
            get
            { return _posMarkerAngle; }
            set
            { _posMarkerAngle = value; }
        }

        public static Dictionary<string, string> messageAlarmCodes = new Dictionary<string, string>();
        public static Dictionary<string, string> messageErrorCodes = new Dictionary<string, string>();
        public static Dictionary<string, string> messageSettingCodes = new Dictionary<string, string>();
        private static readonly StatConvert[] statusConvert = new StatConvert[11];

        // check https://github.com/gnea/grbl/wiki/Grbl-v1.1-Commands#g---view-gcode-parser-state
        public static int[] unknownG = { 41, 64, 81, 83 };
        public static GrblState ParseStatus(string status)    // {idle, run, hold, home, alarm, check, door}
        {
            for (int i = 0; i < statusConvert.Length; i++)
            {
                if (status.StartsWith(statusConvert[i].msg))     // status == statusConvert[i].msg
                    return statusConvert[i].state;
            }
            return GrblState.unknown;
        }
        public static string StatusToText(GrblState state)
        {
            for (int i = 0; i < statusConvert.Length; i++)
            {
                if (state == statusConvert[i].state)
                {
                    if (Properties.Settings.Default.grblTranslateMessage)
                        return statusConvert[i].text;
                    else
                        return statusConvert[i].state.ToString();
                }
            }
            return "Unknown";
        }
        public static Color GrblStateColor(GrblState state)
        {
            for (int i = 0; i < statusConvert.Length; i++)
            {
                if (state == statusConvert[i].state)
                    return statusConvert[i].color;
            }
            return Color.Fuchsia;
        }
        public static bool GetBufferSize(string text)
        {
            if (bufferSize <= 0)    // only get if not done already
            {
                string[] dataValue = text.Split(',');
                int tmp = -1;
                if (dataValue.Length > 1)
                { int.TryParse(dataValue[1], NumberStyles.Any, CultureInfo.InvariantCulture, out tmp); }
                if (tmp > 0)
                    bufferSize = tmp;
                return true;
            }
            return false;
        }
        internal static int GetPosition(int serNr, string text, ref XyzPoint position)
        {
            string[] dataField = text.Split(':');
            if (dataField.Length <= 1)
                return 0;
            string[] dataValue = dataField[1].Split(',');
            //            axisA = false; axisB = false; axisC = false;
            int axisCountLocal = 0;
            if (dataValue.Length == 1)
            {
                Double.TryParse(dataValue[0], NumberStyles.Any, CultureInfo.InvariantCulture, out position.Z);
                position.X = 0;
                position.Y = 0;
            }
            if (dataValue.Length == 2)	// 2021-11-03 just two coordinates
            {
                Double.TryParse(dataValue[0], NumberStyles.Any, CultureInfo.InvariantCulture, out position.X);
                Double.TryParse(dataValue[1], NumberStyles.Any, CultureInfo.InvariantCulture, out position.Y);
                position.Z = 0;
                axisCountLocal = 2;
            }
            if (dataValue.Length > 2)
            {
                Double.TryParse(dataValue[0], NumberStyles.Any, CultureInfo.InvariantCulture, out position.X);
                Double.TryParse(dataValue[1], NumberStyles.Any, CultureInfo.InvariantCulture, out position.Y);
                Double.TryParse(dataValue[2], NumberStyles.Any, CultureInfo.InvariantCulture, out position.Z);
                axisCountLocal = 3;
            }
            if (dataValue.Length > 3)
            {
                Double.TryParse(dataValue[3], NumberStyles.Any, CultureInfo.InvariantCulture, out position.A);
                axisCountLocal++;
                if (serNr == 1) axisA = true;
            }
            if (dataValue.Length > 4)
            {
                Double.TryParse(dataValue[4], NumberStyles.Any, CultureInfo.InvariantCulture, out position.B);
                axisCountLocal++;
                if (serNr == 1) axisB = true;
            }
            if (dataValue.Length > 5)
            {
                Double.TryParse(dataValue[5], NumberStyles.Any, CultureInfo.InvariantCulture, out position.C);
                axisCountLocal++;
                if (serNr == 1) axisC = true;
            }
            if (serNr == 1)
                axisCount = axisCountLocal;
            return axisCountLocal;
            //axisA = true; axisB = true; axisC = true;     // for test only
        }

        internal static void GetOtherFeedbackMessage(string[] dataField)
        {
            string tmp = string.Join(":", dataField);
            if (messages.ContainsKey(dataField[0]))
                messages[dataField[0]] = tmp;
            else
                messages.Add(dataField[0], tmp);
        }



        public static string GetSettingDescription(string msgNr)
        {
            string msg = " no information found '" + msgNr + "'";
            try { msg = Grbl.messageSettingCodes[msgNr]; }
            catch { }
            return msg;
        }
        public static string GetMsgNr(string msg)
        {
            string[] tmp = msg.Split(':');
            if (tmp.Length > 1)
            { return tmp[tmp.Length - 1].Trim(); }      // 2021-05-01 change from [1]
            return "";
        }
        public static string GetErrorDescription(string rxString)
        {   //string[] tmp = rxString.Split(':');
            string msgNr = GetMsgNr(rxString);
            if (msgNr.Length >= 1)
            {
                string msg = " no information found for error-nr. '" + msgNr + "'";
                try
                {
                    if ((messageErrorCodes != null) && messageErrorCodes.ContainsKey(msgNr))
                    {
                        msg = Grbl.messageErrorCodes[msgNr];
                        //int errnr = Convert.ToInt16(tmp[1].Trim());
                        lastErrorNr = 0;
                        lastMessage = rxString + " " + msg;
                        if (!short.TryParse(msgNr, NumberStyles.Any, CultureInfo.InvariantCulture, out lastErrorNr))
                            return msg;
                        if ((lastErrorNr >= 32) && (lastErrorNr <= 34))
                            msg += "\r\n\r\nPossible reason: scale down of GCode with G2/3 commands.\r\nSolution: use more decimal places.";
                    }
                }
                catch { }
                return msg;
            }
            else
            {
                return " no info ";
            }
        }

        public static string GetAlarmDescription(string rxString)
        {
            string[] tmp = rxString.Split(':');
            if (tmp.Length <= 1) return "no info " + tmp;

            string msg = " no information found for alarm-nr. '" + tmp[1] + "'";
            try
            {
                if ((messageAlarmCodes != null) && messageAlarmCodes.ContainsKey(tmp[1].Trim()))
                    msg = Grbl.messageAlarmCodes[tmp[1].Trim()];
            }
            catch { }
            return msg;
        }
        public static string GetRealtimeDescription(int id)
        {
            switch (id)
            {
                case 24:
                    return "Soft-Reset";
                case '?':
                    return "Status Report Query";
                case '~':
                    return "Cycle Start / Resume";
                case '!':
                    return "Feed Hold";
                case 132:
                    return "Safety Door";
                case 133:
                    return "Jog Cancel";
                case 144:
                    return "Set 100% of programmed feed rate.";
                case 145:
                    return "Feed Rate increase 10%";
                case 146:
                    return "Feed Rate decrease 10%";
                case 147:
                    return "Feed Rate increase 1%";
                case 148:
                    return "Feed Rate decrease 1%";
                case 149:
                    return "Set to 100% full rapid rate.";
                case 150:
                    return "Set to 50% of rapid rate.";
                case 151:
                    return "Set to 25% of rapid rate.";
                case 153:
                    return "Set 100% of programmed spindle speed";
                case 154:
                    return "Spindle Speed increase 10%";
                case 155:
                    return "Spindle Speed decrease 10%";
                case 156:
                    return "Spindle Speed increase 1%";
                case 157:
                    return "Spindle Speed decrease 1%";
                case 158:
                    return "Toggle Spindle Stop";
                case 160:
                    return "Toggle Flood Coolant";
                case 161:
                    return "Toggle Mist Coolant";
                default:
                    return "unknown setting " + id.ToString();
            }
        }
    }

    internal enum GrblState { idle, run, hold, jog, alarm, door, check, home, sleep, probe, reset, unknown, Marlin, notConnected };
    internal enum GrblStreaming { ok, error, reset, finish, pause, waitidle, toolchange, stop, lasermode, waitstop, setting };

    internal struct StatConvert
    {
        public string msg;
        public string text;
        internal GrblState state;
        public Color color;
    };

    internal class ParsState
    {
        public bool changed = false;
        public int motion = 0;           // {G0,G1,G2,G3,G38.2,G80} 
        public int feed_rate = 94;       // {G93,G94} 
        public int units = 21;           // {G20,G21} 
        public int distance = 90;        // {G90,G91} 
                                         // uint8_t distance_arc; // {G91.1} NOTE: Don't track. Only default supported. 
        public int plane_select = 17;    // {G17,G18,G19} 
                                         // uint8_t cutter_comp;  // {G40} NOTE: Don't track. Only default supported. 
        public double tool_length = 0;       // {G43.1,G49} 
        public int coord_select = 54;    // {G54,G55,G56,G57,G58,G59} 
                                         // uint8_t control;      // {G61} NOTE: Don't track. Only default supported. 
        public int program_flow = 0;    // {M0,M1,M2,M30} 
        public int coolant = 9;         // {M7,M8,M9} 
        public int spindle = 5;         // {M3,M4,M5} 
        public bool toolchange = false;
        public int tool = 0;            // tool number
        public double FR = 0;           // feedrate
        public double SS = 0;           // spindle speed
        public bool TLOactive = false;// Tool length offset

        public void Reset()
        {
            motion = 0; plane_select = 17; units = 21;
            coord_select = 54; distance = 90; feed_rate = 94;
            program_flow = 0; coolant = 9; spindle = 5;
            toolchange = false; tool = 0; FR = 0; SS = 0;
            TLOactive = false; tool_length = 0;
            changed = false;
        }

    };

    internal class ModState
    {
        public string Bf, Ln, FS, Pn, Ov, A;
        /*   public ModState(string bf, string ln, string fs, string pn, string ov, string a)
           { Bf = bf; Ln = ln; FS = fs; Pn = pn; Ov = ov; A = a; }*/
        public ModState()
        { Clear(); }
        public void Clear()
        { Bf = ""; Ln = ""; FS = ""; Pn = ""; Ov = ""; A = ""; }
    };

    public class StreamEventArgs : EventArgs
    {
        private readonly int codeFinish;
        private readonly int buffFinish;
        private readonly int codeLineSent;
        private readonly int codeLineConfirmed;
        private readonly GrblStreaming status;
        internal StreamEventArgs(int c1, int c2, int a1, int a2, GrblStreaming stat)
        {
            codeLineSent = c1;
            codeLineConfirmed = c2;
            codeFinish = a1;
            buffFinish = a2;
            status = stat;
        }
        public int CodeLineSent
        { get { return codeLineSent; } }
        public int CodeLineConfirmed
        { get { return codeLineConfirmed; } }
        public int CodeProgress
        { get { return codeFinish; } }
        public int BuffProgress
        { get { return buffFinish; } }
        internal GrblStreaming Status
        { get { return status; } }
    }

    public class PosEventArgs : EventArgs
    {
        private XyzPoint posWorld, posMachine;
        private readonly GrblState status;
        private readonly ModState statMsg;
        private readonly ParsState lastCmd;
        private readonly string raw;
        internal PosEventArgs(XyzPoint world, XyzPoint machine, GrblState stat, ModState msg, ParsState last, string sraw)
        {
            posWorld = world;
            posMachine = machine;
            status = stat;
            statMsg = msg;
            lastCmd = last;
            raw = sraw;
        }
        internal XyzPoint PosWorld
        { get { return posWorld; } }
        internal XyzPoint PosMachine
        { get { return posMachine; } }
        internal GrblState Status
        { get { return status; } }
        internal ModState StatMsg
        { get { return statMsg; } }
        internal ParsState ParserState
        { get { return lastCmd; } }
        public string Raw
        { get { return raw; } }
    }
}
