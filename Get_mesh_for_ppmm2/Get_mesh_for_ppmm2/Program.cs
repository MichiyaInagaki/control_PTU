﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Get_mesh_for_ppmm2
{
    class Program
    {
        //各種初期設定*************************************************************************************************
        //////CSVファイルパス//////////////////////////////////////////////////////////////////////
        private static string csv_path = @"C:\Users\SENS\source\repos\Control_PTU\Control_PTU\csv\simulation\";    //***読み込み場所***
        private static string LMm_file_name = "2018_11_23 134457";                //***ファイル名***
        private static int Threshold = 80;                                        //***フラグの閾値***
        private static string output_path = csv_path + "Split_OP11.csv";            //出力先のパス(最終配列)***
        private static string output_path_y = csv_path + "Split_OP_y11.csv";        //出力先のパス（測定値）***
        private static string output_path_mesh = csv_path + "mesh11.csv";      //出力先のパス（mesh行列）***
        //////同期時間の設定///////////////////////////////////////////////////////////////////////
        private static TimeSpan initial_offset = new TimeSpan(0, 0, 0, 0, 400);  //TimeSpan(日，時間，分，秒，ミリ秒)    //時刻切り上げ分
        private static TimeSpan interval_PTU = new TimeSpan(0, 0, 0, 0, 200);    //PTUの時間の正規化間隔
        private static TimeSpan interval_LMm = new TimeSpan(0, 0, 0, 0, 200);    //LMmの時間の正規化間隔
        ///////ロボットプラットフォームの寸法設定[m]///////////////////////////////////////////////
        private static double Height = 0.273 + 0.01 + 0.091;            //PTUの高さ[m]=移動ロボットの高さ+固定盤の厚み+PTUの腕関節までの長さ
        private static double length_tilt = 0.038 + 0.01 + 0.02;        //Tilt部分の腕の長さ+取り付け具の厚み+メタン計の中心まで[m]
        private static double length_pan = 0.019;                       //レーザーの発射口とファイ回転軸中心からのずれ[m]
        private static double length_LMmG = 0.09;                       //回転中心からのLMｍ－Gの長さ[m](=LMm-Gの長さの半分)
        private static double resolition = 185.1429;                    //分解能
        ///////計測範囲，計測の刻み幅（!!計測時と同じもの!!ここは共通項!!）/////////////////////
        private static int measure_point = 11;   //***★計測ポイントの数★***
        private static double delta = 0.2;       //刻み幅
        private static double xmin = -1.0;       //deltaで割り切れるもの！
        private static double xmax = 1.0;        //deltaで割り切れるもの！
        private static double ymin = 0.6;
        private static double ymax = 2.6;
        private static double zmax = Height + length_tilt;
        //ボクセル設定用////////////////////////////////////////////////////////////////////////////
        private static int Xrange = RangetoNum(xmax) - RangetoNum(xmin);            //x方向のセルの数
        private static int Yrange = RangetoNum(ymax);                                  //y方向のセルの数　※yminは考慮しない（LMm-Gの始点はy=0にあるため）
        private static int Zrange = RangetoNum(RoundUp(Height + length_tilt, delta));  //z方向のセルの数（PTUの高さを分解能で切り上げたもの）
        private static int cell_size = Xrange * Yrange * Zrange;                          //すべてのセルの数
        ///////計測点の総数////////////////////////////////////////////////////////////////////////
        private static int measure_num = (Xrange + 1) * (RangetoNum(ymax) - RangetoNum(ymin) + 1);    //\\\default
        //private static int measure_num = 22;  //CS用
        private static int MP = 0;      //計測回数カウント用
        private static int MN = 0;      //計測ナンバー格納用
        //光路の分割数の最大値（仮）///////////////////////////////////////////////////////////////
        private static int temp_len = Xrange + Yrange + Zrange;
        //交点を表現するための構造体の定義/////////////////////////////////////////////////////////
        public struct INTERSECTION
        {
            public double x;   //x座標
            public double y;   //y座標
            public double z;   //z座標
            public double len; //原点からの距離
            public double op;  //分割光路長
            public int num;    //ボクセルナンバー
        }
        //計測範囲を渡すための構造体の定義/////////////////////////////////////////////////////////
        public struct RANGE
        {
            public double xmin;
            public double xmax;
            public double ymin;
            public double ymax;
        }
        //メッシュの刻み幅//////////////////////////////////////////////////////////////////////////
        private static double r_size = 0.05;     //光路上の点を分割する距離***
        private static double temp_mesh_size = Math.Sqrt((xmax - xmin) * (xmax - xmin) + (ymax * ymax) + zmax * zmax);  //最大の分割数を包括するために計測領域の対角の長さを計算し，
        private static int mesh_size = 3*(int)(temp_mesh_size / r_size)+1;                                              //分割距離で割ることで，格納するメッシュの数（分割数）を算出
        //分割光路を格納する配列[計測点の数,ボクセルの数]=OP //これが欲しいデータリスト!////////////
        private static int temp_MP = measure_num * measure_point;
        private static double[,] split_OP = new double[temp_MP, cell_size];     //split_OP[計測数，分割光路長]
        private static double[,] MESH = new double[temp_MP, mesh_size];         //MESH[計測数（一本のパス），メッシュ情報（光路長＋分割点のxyz座標）]
        //測定値を格納する配列
        private static double[] LMm_value = new double[temp_MP];
        //LMmデータの受け渡し用
        private static DateTime dt_flagtime_LMm;
        private static int Length_LMmList;
        private static List<DateTime> LMmtimestamp;
        private static List<int> LMmmeasure;
        //PTUフラグ時刻用
        private static DateTime dt_flagtime_PTU;
        //END各種初期設定*********************************************************************************************


        //各種関数，静的変数など**************************************************************************************
        //時間の正規化のための関数///////////////////////////////////////////////////////////////////
        //切り上げ
        public static DateTime Time_RoundUp(DateTime input, TimeSpan interval)
            => new DateTime(((input.Ticks + interval.Ticks - 1) / interval.Ticks) * interval.Ticks, input.Kind);
        //切り下げ
        public static DateTime Time_RoundDown(DateTime input, TimeSpan interval)
            => new DateTime((((input.Ticks + interval.Ticks) / interval.Ticks) - 1) * interval.Ticks, input.Kind);
        //数値の丸めを行う関数///////////////////////////////////////////////////////////////////////
        //切り上げ(データ，切り上げ間隔)
        public static double RoundUp(double data, double interval)
        {
            if (data >= 0)
            {
                return (int)((data + interval - 0.0000000000001) / interval) * interval;    //-0.001は2.0ジャストなどを2とするためのもの
            }
            else
            {
                return (int)((data - interval + 0.0000000000001) / interval) * interval;
            }
        }
        //切り捨て（データ，切り捨て間隔）
        public static double RoundDown(double data, double interval)
        {
            return (int)(data / interval) * interval;
        }
        //int型へのキャスト問題を解消するための関数：(int)(0.3/0.1)=0.2となる問題
        public static int RangetoNum(double d_num)
        {
            if (d_num >= 0)
            {
                return (int)(d_num / delta + 0.0001);
            }
            else
            {
                return (int)(d_num / delta - 0.0001);
            }
        }
        //END各種関数，静的変数など***********************************************************************************

        static void Main(string[] args)
        {

            int i, j;

            //初期化関係*****************************************************************************************
            //分割光路長を格納する配列の初期化
            for (i = 0; i < measure_num * measure_point; i++)
            {
                for (j = 0; j < cell_size; j++)
                {
                    split_OP[i, j] = 0;
                }
            }
            //
            Console.WriteLine("cellsize:" + cell_size + " measure_num:" + measure_num + " zrannge:" + Zrange);
            Console.WriteLine("r_size:" + r_size +  " mesh_size:" + mesh_size);
            //
            //メッシュ情報を格納する配列の初期化
            for (i = 0; i < measure_num * measure_point; i++)
            {
                for (j = 0; j < mesh_size; j++)
                {
                    MESH[i, j] = -1;
                }
            }
            //ボクセルナンバーをふる
            create_VOXEL_num();
            //END初期化関係**************************************************************************************


            //LMmデータとPTUデータの同期処理*********************************************************************
            //方針：①LMmの全データ取得，②PTUの全データ取得，③フラグ時刻を用いて①②を同期させ，///
            //「LMmの値＋そのときのPTUの角度情報」のデータセットを作成する                        ///
            /////////////////////////////////////////////////////////////////////////////////////////

            //LMm-Gデータ読み込み/////////////////////////////////////////////////////////////////////////////////
            ///////////////////////////////////////////////////////////////////////////////////////
            ////    LMmtimestamp : DateTime型 : LMmのタイムスタンプ [yyyy/MM/dd/HH:mm:ss.fff]  ////
            ////    LMmmeasure   : int型      : LMmの計測値 [ppm-m]                            //// 
            ///////////////////////////////////////////////////////////////////////////////////////

            //LMm-Gデータをタイムスタンプ，測定値に分けて取得
            List<string> str_LMmtimestamp = new List<string>();      //LMmのタイムスタンプ格納用リスト
            List<string> str_LMmmeasure = new List<string>();        //LMmの測定値格納用リスト

            //CSV読み込み
            string filePath_LMm = csv_path + LMm_file_name + ".csv";
            //一本目のファイルを読み込む
            StreamReader reader2 = new StreamReader(filePath_LMm, Encoding.GetEncoding("Shift_JIS"));
            for (int p = 0; p < 7; p++)     //冒頭のファイル情報等を飛ばす
            {
                reader2.ReadLine();
            }
            bool m_temp = false;
            string str_flagtime_LMm = null; //フラグ時刻格納用
            while (m_temp == false)         //フラグの場所まで進む
            {
                string[] str_temp = reader2.ReadLine().Split(',');
                if (int.Parse(str_temp[2]) > Threshold)
                {
                    m_temp = true;
                    str_flagtime_LMm = str_temp[1];     //フラグ時刻の取得
                }
            }
            dt_flagtime_LMm = DateTime.ParseExact(str_flagtime_LMm,          //stringをDateTimeに変換
            "yyyy/MM/dd HH:mm:ss.f",
            System.Globalization.DateTimeFormatInfo.InvariantInfo,
            System.Globalization.DateTimeStyles.None);
            //Console.WriteLine("FlagTime_LMm: " + dt_flagtime_LMm.ToString("yyyy/MM/dd/HH:mm:ss.fff"));

            while (reader2.Peek() >= 0)     //一本目のLMm本データ読み込み
            {
                string[] cols = reader2.ReadLine().Split(',');
                str_LMmtimestamp.Add(cols[1]);
                str_LMmmeasure.Add(cols[3]);
            }
            reader2.Close();

            //続きのファイルを読み込む
            int temp_num = 1;
            while (true)
            {
                filePath_LMm = csv_path + LMm_file_name + "_" + temp_num.ToString() + ".csv";
                if (File.Exists(filePath_LMm))
                {
                    StreamReader reader3 = new StreamReader(filePath_LMm, Encoding.GetEncoding("Shift_JIS"));
                    for (int p = 0; p < 7; p++)     //冒頭のファイル情報等を飛ばす
                    {
                        reader3.ReadLine();
                    }
                    while (reader3.Peek() >= 0)     //LMm本データ読み込み
                    {
                        string[] cols = reader3.ReadLine().Split(',');
                        str_LMmtimestamp.Add(cols[1]);
                        str_LMmmeasure.Add(cols[3]);
                    }
                    reader3.Close();
                    temp_num++;
                }
                else
                {
                    break;  //該当ファイルがなくなれば読み込み終了
                }
            }

            Length_LMmList = str_LMmtimestamp.Count;            //行数を得る

            //string型からのキャスト
            LMmtimestamp = str_LMmtimestamp.ConvertAll(x => System.DateTime.ParseExact(x,
            "yyyy/MM/dd HH:mm:ss.f",
            System.Globalization.DateTimeFormatInfo.InvariantInfo,
            System.Globalization.DateTimeStyles.None));                             //LMmのタイムスタンプ
            LMmmeasure = str_LMmmeasure.ConvertAll(x => int.Parse(x));    //LMm計測データ

            //時間の正規化
            LMmtimestamp = LMmtimestamp.ConvertAll(x => Time_RoundDown(x, interval_LMm));

            //リストの表示確認***
            //Console.WriteLine("■LMm-Gデータの表示");  //***
            //for (i = 0; i < Length_LMmList; i++)
            //{
            //    Console.WriteLine("LMm_TIME:" + LMmtimestamp[i].ToString("yyyy/MM/dd/HH:mm:ss.fff") + " LMm_measure:" + LMmmeasure[i]);
            //}
            //Console.WriteLine();
            //Console.WriteLine("LMmデータの長さ:" + Length_LMmList);
            //END:LMm-Gデータ読み込み******************************************************************************

            //PTUのフラグ時刻取得///////////////////////////////////////////////////////////////////////
            string filePath = csv_path + "Flag_time.csv";    //フラグ時刻のファイル読み込み
            StreamReader reader4 = new StreamReader(filePath, Encoding.GetEncoding("Shift_JIS"));
            string str_Flagtime_PTU = reader4.ReadLine();                    //フラグ時刻の取得   
            dt_flagtime_PTU = DateTime.ParseExact(str_Flagtime_PTU,          //stringをDateTimeに変換
            "yyyy/MM/dd/HH:mm:ss.fff",
            System.Globalization.DateTimeFormatInfo.InvariantInfo,
            System.Globalization.DateTimeStyles.None);
            //Console.WriteLine("FlagTime_PTU: " + dt_flagtime_PTU.ToString("yyyy/MM/dd/HH:mm:ss.fff"));
            ////////////////////////////////////////////////////////////////////////////////////////////

            //実行部へ
            ////////////////////////Exercute関数の仕様//////////////////////////////////////////
            //Exercute(PTU_file_path, LMm_file_path, フラグ用の閾値)                          //
            //PTUのデータファイルを一つずつ読み込んで実行していく                             //
            ////////////////////////////////////////////////////////////////////////////////////
            int temp_num2 = 0;
            while (true)
            {
                string PTU_file_path = csv_path + "MP" + temp_num2.ToString() + ".csv";        //PTUのファイルパス
                if (File.Exists(PTU_file_path))
                {
                    Exercute(PTU_file_path);    //実行部へ
                    temp_num2++;
                }
                else
                {
                    break;  //該当ファイルがなくなれば終了
                }
            }

            //Console.WriteLine("measurenum: " + measure_num * measure_point);


            //CSV出力関係**************************************************************************************
            //最終配列の書き出し////////////////////////////////////////////////////////////////
            try
            {
                // appendをtrueにすると，既存のファイルに追記
                //         falseにすると，ファイルを新規作成する
                var append = true;
                // 出力用のファイルを開く
                using (var sw = new StreamWriter(output_path, append))
                {
                    for (i = 0; i < measure_num * measure_point; i++)
                    {
                        for (j = 0; j < cell_size; j++)
                        {
                            sw.Write(split_OP[i, j] + ",");   //書き込み
                        }
                        sw.WriteLine();  //改行
                    }
                }
            }
            catch (Exception err)
            {
                // ファイルを開くのに失敗したときエラーメッセージを表示
                Console.WriteLine(err.Message);
            }
            ////測定値の書き出し/////////////////////////////////////////////////////////////////
            try
            {
                // appendをtrueにすると，既存のファイルに追記
                //         falseにすると，ファイルを新規作成する
                var append = true;
                // 出力用のファイルを開く
                using (var sw = new StreamWriter(output_path_y, append))
                {
                    for (i = 0; i < measure_num * measure_point; i++)
                    {
                        sw.WriteLine(LMm_value[i]);  //書き込み
                    }
                }
            }
            catch (Exception err)
            {
                // ファイルを開くのに失敗したときエラーメッセージを表示
                Console.WriteLine(err.Message);
            }
            //MESH配列の書き出し////////////////////////////////////////////////////////////////
            try
            {
                // appendをtrueにすると，既存のファイルに追記
                //         falseにすると，ファイルを新規作成する
                var append = true;
                // 出力用のファイルを開く
                using (var sw = new StreamWriter(output_path_mesh, append))
                {
                    for (i = 0; i < measure_num * measure_point; i++)
                    {
                        for (j = 0; j < mesh_size; j++)
                        {
                            sw.Write(MESH[i, j] + ",");   //書き込み
                        }
                        sw.WriteLine();  //改行
                    }
                }
            }
            catch (Exception err)
            {
                // ファイルを開くのに失敗したときエラーメッセージを表示
                Console.WriteLine(err.Message);
            }
            /////////////////////////////////////////////////////////////////////////////////////
        }


        

        //実行関数（ファイル読み込み，分割光路の計算）****************************************************************
        //_xminなどは局所的なもの!!
        private static void Exercute(string PTU_file_path)
        {
            int i;

            //PTUデータの読み込み********************************************************************************
            ///////////////////////////////////////////////////////////////////////////////////////
            ////    PTUtimestamp : DateTime型 : PTUのタイムスタンプ [yyyy/MM/dd/HH:mm:ss.fff]  ////
            ////    pos_pan      : int型      : Panポジション [position]                       //// 
            ////    pos_tilt     : int型      : Tiltポジション [position]                      ////
            ///////////////////////////////////////////////////////////////////////////////////////

            ////PTUデータをタイムスタンプ，パン角，チルト角の要素に分けて取得
            List<string> str_PTUtimestamp = new List<string>();
            List<string> str_pos_pan = new List<string>();
            List<string> str_pos_tilt = new List<string>();

            //CSV読み込み
            string filePath = PTU_file_path;
            StreamReader reader = new StreamReader(filePath, Encoding.GetEncoding("Shift_JIS"));
            string[] range_PTU = reader.ReadLine().Split(',');          //一行目読み込み(計測範囲の取得)
            double _xmin = double.Parse(range_PTU[0]);
            double _xmax = double.Parse(range_PTU[1]);
            double _ymin = double.Parse(range_PTU[2]);
            double _ymax = double.Parse(range_PTU[3]);
            //値渡し用
            RANGE range;
            range.xmin = _xmin;
            range.xmax = _xmax;
            range.ymin = _ymin;
            range.ymax = _ymax;

            while (reader.Peek() >= 0)      //PTU本データ取得
            {
                string[] cols = reader.ReadLine().Split(',');
                str_PTUtimestamp.Add(cols[0]);
                str_pos_pan.Add(cols[1]);
                str_pos_tilt.Add(cols[2]);
            }
            reader.Close();

            //string型からのキャスト
            List<DateTime> PTUtimestamp = str_PTUtimestamp.ConvertAll(x => System.DateTime.ParseExact(x,
            "yyyy/MM/dd/HH:mm:ss.fff",
            System.Globalization.DateTimeFormatInfo.InvariantInfo,
            System.Globalization.DateTimeStyles.None));                              //PTUのタイムスタンプ
            List<int> pos_pan = str_pos_pan.ConvertAll(x => int.Parse(x));           //Pan角
            List<int> pos_tilt = str_pos_tilt.ConvertAll(x => int.Parse(x));         //Tilt角                                                                             
            int length_PTUdata = PTUtimestamp.Count;     //リストの長さの取得
            //PTUリストの表示確認***
            //Console.WriteLine("■PTUデータの表示");    //***
            //for (i = 0; i < length_PTUdata; i++)
            //{
            //    Console.WriteLine("PTU_TIME:" + PTUtimestamp[i].ToString("yyyy/MM/dd/HH:mm:ss.fff") + " PAN:" + pos_pan[i] + " TILT:" + pos_tilt[i]);
            //}
            //Console.WriteLine();
            //END:PTUデータ読み込み*******************************************************************************

            //時間同期させたデータセットの作成********************************************************************
            //////////////////////////////////////////////////////////////////////////////////////////////////
            ////    Synchro_time : List<DateTime>型 : 同期時刻のタイムスタンプ [yyyy/MM/dd/HH:mm:ss.fff]  ////
            ////    PTU_Pan      : List<int>型      : Pan  [position]                                     //// 
            ////    PTU_Tilt     : List<int>型      : Tilt [position]                                     ////
            ////    LMm_Measure  : List<int>型      : LMm-Gの測定値 [ppm-m]                               ////
            //////////////////////////////////////////////////////////////////////////////////////////////////

            int PTU_data_num = 0, LMm_data_num = 0;
            List<int> PTU_Pan = new List<int>();            //PTUのPan角を格納
            List<int> PTU_Tilt = new List<int>();           //PTUのTilt角を格納
            List<int> LMm_Measure = new List<int>();        //LMm-Gの計測データを格納
            List<DateTime> Synchro_time = new List<DateTime>(); //同期時刻データを格納
            i = 0;
            TimeSpan span = new TimeSpan();                     //タイムスタンプの差を格納
            TimeSpan null_time = new TimeSpan(0, 0, 0, 0, 0);   //NULL時間
            TimeSpan offset = (dt_flagtime_LMm - dt_flagtime_PTU) + initial_offset;  //TimeSpan(日，時間，分，秒，ミリ秒)    //PTUとAndroidの時間ずれ***
            //Console.WriteLine("■offset:" + offset + "\n");      //offsetの表示 
            //***PTUとAndroidの時間ずれ(オフセット)の修正***
            PTUtimestamp = PTUtimestamp.ConvertAll(x => x + offset);

            //時間の正規化（数値の丸め, 切り上げ）
            PTUtimestamp = PTUtimestamp.ConvertAll(x => Time_RoundUp(x, interval_PTU));

            for (PTU_data_num = 0; PTU_data_num < length_PTUdata; PTU_data_num++)       //PTUデータを走査していく
            {
                for (; LMm_data_num < Length_LMmList; LMm_data_num++)                  //LMmデータを走査していく
                {
                    span = PTUtimestamp[PTU_data_num] - LMmtimestamp[LMm_data_num];     //タイムスタンプの時間差を取得

                    if (span == null_time)      //PTUとLMｍの時間が同じとき，データを格納する
                    {
                        PTU_Pan.Add(pos_pan[PTU_data_num]);
                        PTU_Tilt.Add(pos_tilt[PTU_data_num]);
                        LMm_Measure.Add(LMmmeasure[LMm_data_num]);
                        Synchro_time.Add(PTUtimestamp[PTU_data_num]);
                        break;                  //現在のPTUデータと一致するのは1つなので，一致すればこのループを抜ける
                    }

                    if ((int)span.TotalMilliseconds < 0)
                    {
                        break;                  //現在のPTUデータと一致するものがなければ，次のPTUデータへ
                    }
                }
            }

            //リストの表示確認
            int Length_List = Synchro_time.Count;              //行数を得る=一地点からの計測点数
            //Console.WriteLine("■同期済みデータの表示");     //***
            //for (i = 0; i < Length_List; i++)
            //{
            //    Console.WriteLine("num:" + (i + 1) + " TIME:" + Synchro_time[i].ToString("yyyy/MM/dd/HH:mm:ss.fff") + " Pan:" + PTU_Pan[i] + " Tilt:" + PTU_Tilt[i] + " LMm_Measure:" + LMm_Measure[i]);
            //}
            //Console.WriteLine();
            
            //END時間同期させたデータセットの作成*********************************************************************


            //分割光路の計算******************************************************************************************

            //計算部
            //MN: 計測番号（初期化処理しない）
            for (i = 0; i < Length_List; i++, MN++)
            {
                //分割光路の計算
                CalOP(PTU_Pan[i], PTU_Tilt[i], MN, range);      //CalOP(Pan角，Tilt角，計測ナンバー（全計測数），計測範囲)
                //測定値の登録
                LMm_value[MN] = LMm_Measure[i];
            }

            //最終配列の表示確認
            //for (i = 0; i < measure_num; i++)
            //{
            //    for (j = 1; j < cell_size + 1; j++)
            //    {
            //        Console.WriteLine("measure_num:" + i + " cell_num:" + j + " OP:" + split_OP[i, j]);
            //    }
            //}

            //END分割光路の計算***************************************************************************************

        }
        //END実行関数*************************************************************************************************



        //分割光路長の計算********************************************************************************************
        private static void CalOP(int pan, int tilt, int MEASURE_NUMBER, RANGE range)
        {
            int i;
            //計測範囲取得//////////////////////////////////////////////////////////
            double _xmin = range.xmin;
            double _xmax = range.xmax;
            double _ymin = range.ymin;
            double _ymax = range.ymax;
            ///////分解能の設定，角度変換用の数値///////////////////////////////////
            double degpos = resolition / 3600;  //[degree/position]
            double deg_pan, deg_tilt, rad_pan, rad_tilt;
            double deg_pan_modify, rad_pan_modify;
            //角度変換[pos]→[deg] //PTU座標軸から平面グリッド座標軸に変換:[[-1かける]],[[90°から引く]]
            deg_tilt = 90 - (-tilt * degpos);
            if (-pan * degpos >= 0)      //phi>0,phi<0で場合分け
            {
                deg_pan = 90 - (-pan * degpos);
            }
            else
            {
                deg_pan = 90 - (pan * degpos);
            }
            //角度変換[deg]→[rad] //＋ファイドット，シータドットに変換
            rad_pan = deg_pan * (Math.PI / 180);
            rad_tilt = deg_tilt * (Math.PI / 180);
            //角度計算用（角度補正）
            double r_len = Math.Sqrt(zmax * zmax + (zmax * Math.Tan(rad_tilt)) * (zmax * Math.Tan(rad_tilt)));      //r:光路全長
            double rad_beta = Math.Atan2(length_pan, Math.Sqrt(r_len * r_len - length_pan * length_pan));           //Beta: 中心からのファイずれの修正角
            double rad_gamma, x_origin, y_origin;
            if (-pan * degpos >= 0)      //phi>0,phi<0で場合分け 
            {
                rad_gamma = (Math.PI / 2) - rad_pan - rad_beta;    //光源点を出すための角度
                x_origin = length_pan * Math.Cos(rad_gamma);    //光源点のx座標
                y_origin = -length_pan * Math.Sin(rad_gamma);    //光源点のy座標
            }
            else
            {
                rad_gamma = (Math.PI / 2) - (rad_pan - rad_beta);    //光源点を出すための角度
                x_origin = length_pan * Math.Cos(rad_gamma);    //光源点のx座標
                y_origin = length_pan * Math.Sin(rad_gamma);    //光源点のy座標
            }
            if (-pan * degpos >= 0)      //phi>0,phi<0で場合分け
            {
                rad_pan_modify = rad_pan + rad_beta;
            }
            else
            {
                rad_pan_modify = rad_pan - rad_beta;
            }
            //Console.WriteLine(" x_origin:" + x_origin + " y_origin:" + y_origin+" z_origin:"+zmax+" pan"+deg_pan+" tilt:"+deg_tilt);
            ///////ボクセル座標とボクセルナンバーを格納する構造体///////////////////
            INTERSECTION[] intersection_grid = new INTERSECTION[temp_len];
            //構造体の初期化
            for (i = 0; i < temp_len; i++)
            {
                intersection_grid[i].x = 0;
                intersection_grid[i].y = 0;
                intersection_grid[i].z = 0;
                intersection_grid[i].len = 1000;    //flag用に1000に設定
                intersection_grid[i].op = 1000;
                intersection_grid[i].num = -1;
            }
            //分割点の取得で使うもの
            int x_loop, y_loop, z_loop;     //ループ用変数
            double x_grid, y_grid, z_grid;  //一時的に交点の座標が入るもの
            int split_num = 0;              //分割数の初期化
            ////////////////////////////////////////////////////////////////////////


            //分割点の取得//////////////////////////////////////////////////////////
            if (-pan * degpos >= 0)      //x>=0のとき-------------------------------
            {
                x_loop = 1;     //xループの開始点
                if (_xmin > 0)   //_xminが正ならxループの開始点を変更する
                {
                    x_loop = RangetoNum(_xmin);
                }

                //x走査
                for (; x_loop <= RangetoNum(_xmax); x_loop++)  // x_loop = 1 は delta/delta のこと(ループ用に整数に直しているだけ)
                {
                    x_grid = x_loop * delta;    //グリッド座標に変換
                    y_grid = x_grid * Math.Tan(rad_pan_modify) - (length_pan / Math.Cos(rad_pan_modify));
                    z_grid = zmax - Math.Sqrt((x_grid - x_origin) * (x_grid - x_origin) + (y_grid - y_origin) * (y_grid - y_origin)) / Math.Tan(rad_tilt);
                    if (y_grid > _ymax || z_grid < 0)   //y_grid, z_gridが最終座標を超えればbreak
                    {
                        break;
                    }
                    //データ登録
                    intersection_grid[split_num].x = x_grid;
                    intersection_grid[split_num].y = y_grid;
                    intersection_grid[split_num].z = z_grid;
                    intersection_grid[split_num].len = Math.Sqrt((x_grid - x_origin) * (x_grid - x_origin) + (y_grid - y_origin) * (y_grid - y_origin) + (zmax - z_grid) * (zmax - z_grid));
                    intersection_grid[split_num].num = get_VOXEL_num(x_grid, y_grid, z_grid, _xmin, _xmax);
                    split_num++;
                }
                //y走査
                for (y_loop = 1; y_loop <= RangetoNum(_ymax); y_loop++)
                {
                    y_grid = y_loop * delta;    //グリッドに変換
                    x_grid = y_grid / Math.Tan(rad_pan_modify) + (length_pan / Math.Sin(rad_pan_modify));
                    z_grid = zmax - Math.Sqrt((x_grid - x_origin) * (x_grid - x_origin) + (y_grid - y_origin) * (y_grid - y_origin)) / Math.Tan(rad_tilt);
                    if (x_grid > _xmax || z_grid < 0)
                    {
                        break;      //最終座標を超えればブレイク
                    }
                    //データ登録
                    intersection_grid[split_num].x = x_grid;
                    intersection_grid[split_num].y = y_grid;
                    intersection_grid[split_num].z = z_grid;
                    intersection_grid[split_num].len = Math.Sqrt((x_grid - x_origin) * (x_grid - x_origin) + (y_grid - y_origin) * (y_grid - y_origin) + (zmax - z_grid) * (zmax - z_grid));
                    intersection_grid[split_num].num = get_VOXEL_num(x_grid, y_grid, z_grid, _xmin, _xmax);
                    split_num++;
                }
                //z走査
                for (z_loop = Zrange - 1; z_loop >= 0; z_loop--)
                {
                    z_grid = z_loop * delta;     //グリッドに変換
                    y_grid = (zmax - z_grid) * Math.Tan(rad_tilt) * Math.Sin(rad_pan_modify) - (length_pan * Math.Cos(rad_pan_modify));
                    x_grid = (zmax - z_grid) * Math.Tan(rad_tilt) * Math.Cos(rad_pan_modify) + (length_pan * Math.Sin(rad_pan_modify));

                    if (x_grid > _xmax || y_grid > _ymax)
                    {
                        break;        //最終座標を超えればブレイク
                    }
                    //if (x_grid > _xmax)
                    //{
                    //    x_grid = _xmax - 0.001;      //境界の特別処理（z=0の時の誤差を丸め込む）
                    //}
                    //if (y_grid > _ymax)
                    //{
                    //    y_grid = _ymax;
                    //}
                    //データ登録
                    intersection_grid[split_num].x = x_grid;
                    intersection_grid[split_num].y = y_grid;
                    intersection_grid[split_num].z = z_grid;
                    intersection_grid[split_num].len = Math.Sqrt((x_grid - x_origin) * (x_grid - x_origin) + (y_grid - y_origin) * (y_grid - y_origin) + (zmax - z_grid) * (zmax - z_grid));
                    intersection_grid[split_num].num = get_VOXEL_num(x_grid, y_grid, z_grid, _xmin, _xmax);
                    split_num++;
                }
            }
            else    //x<0のとき---------------------------------------------------------------------------
            {
                x_loop = 1;     //xループの開始点
                if (_xmax < 0)   //_xmaxが負ならxループの開始点を変更する
                {
                    x_loop = -RangetoNum(_xmax);
                }

                //x走査
                for (; x_loop <= -RangetoNum(_xmin); x_loop++)  // x_loop = 1 は delta/delta のこと(ループ用に整数に直しているだけ)
                {
                    x_grid = x_loop * delta;    //グリッド座標に変換    
                    y_grid = x_grid * Math.Tan(rad_pan_modify) + (length_pan / Math.Cos(rad_pan_modify));
                    z_grid = zmax - Math.Sqrt((x_grid - x_origin) * (x_grid - x_origin) + (y_grid - y_origin) * (y_grid - y_origin)) / Math.Tan(rad_tilt);
                    if (y_grid > _ymax || z_grid < 0)   //y_grid, z_gridが最終座標を超えればbreak
                    {
                        break;
                    }
                    //データ登録
                    intersection_grid[split_num].x = -x_grid;   //xは絶対値だったので負にする
                    intersection_grid[split_num].y = y_grid;
                    intersection_grid[split_num].z = z_grid;
                    intersection_grid[split_num].len = Math.Sqrt((x_grid - x_origin) * (x_grid - x_origin) + (y_grid - y_origin) * (y_grid - y_origin) + (zmax - z_grid) * (zmax - z_grid));
                    intersection_grid[split_num].num = get_VOXEL_num(-x_grid, y_grid, z_grid, _xmin, _xmax);
                    split_num++;
                }
                //y走査
                for (y_loop = 1; y_loop <= RangetoNum(_ymax); y_loop++)
                {
                    y_grid = y_loop * delta;    //グリッドに変換
                    x_grid = -y_grid / Math.Tan(rad_pan_modify) + (length_pan / Math.Sin(rad_pan_modify));      //xを負にする
                    z_grid = zmax - Math.Sqrt((x_grid - x_origin) * (x_grid - x_origin) + (y_grid - y_origin) * (y_grid - y_origin)) / Math.Tan(rad_tilt);
                    if (x_grid < _xmin || z_grid < 0)
                    {
                        break;              //最終座標を超えればブレイク
                    }
                    //データ登録
                    intersection_grid[split_num].x = x_grid;
                    intersection_grid[split_num].y = y_grid;
                    intersection_grid[split_num].z = z_grid;
                    intersection_grid[split_num].len = Math.Sqrt((x_grid - x_origin) * (x_grid - x_origin) + (y_grid - y_origin) * (y_grid - y_origin) + (zmax - z_grid) * (zmax - z_grid));
                    intersection_grid[split_num].num = get_VOXEL_num(x_grid, y_grid, z_grid, _xmin, _xmax);
                    split_num++;
                }
                //z走査
                for (z_loop = Zrange - 1; z_loop >= 0; z_loop--)
                {
                    z_grid = z_loop * delta;     //グリッドに変換
                    y_grid = (zmax - z_grid) * Math.Tan(rad_tilt) * Math.Sin(rad_pan_modify) + (length_pan * Math.Cos(rad_pan_modify));
                    x_grid = -(zmax - z_grid) * Math.Tan(rad_tilt) * Math.Cos(rad_pan_modify) + (length_pan * Math.Sin(rad_pan_modify));     //xを負にする

                    if (x_grid < _xmin || y_grid > _ymax)
                    {
                        break;
                    }
                    //if (x_grid < _xmin)
                    //{
                    //    x_grid = _xmin+0.001;      //境界の特別処理（z=0の時の誤差を丸め込む）//+0.01は0にしないための処理
                    //}
                    //if (y_grid > _ymax)
                    //{
                    //    y_grid = _ymax;
                    //}

                    //データ登録
                    intersection_grid[split_num].x = x_grid;
                    intersection_grid[split_num].y = y_grid;
                    intersection_grid[split_num].z = z_grid;
                    intersection_grid[split_num].len = Math.Sqrt((x_grid - x_origin) * (x_grid - x_origin) + (y_grid - y_origin) * (y_grid - y_origin) + (zmax - z_grid) * (zmax - z_grid));
                    intersection_grid[split_num].num = get_VOXEL_num(x_grid, y_grid, z_grid, _xmin, _xmax);
                    split_num++;
                }
            }

            //ソート前のデータ確認
            //for (i = 0; i < temp_len; i++)
            //{
            //    Console.WriteLine("num:" + i + " x" + intersection_grid[i].x + " y:" + intersection_grid[i].y + " z:" + intersection_grid[i].z + " len:" + intersection_grid[i].len + " num:" + intersection_grid[i].num);
            //}
            //Console.WriteLine("SORT");

            //原点からの長さでソート//////////////////////////////////////////////////////////////
            SORT(intersection_grid, temp_len);
            //ソート確認
            //for (i = 0; i < temp_len; i++)
            //{
            //    Console.WriteLine("num:" + i + " x" + intersection_grid[i].x + " y:" + intersection_grid[i].y + " z:" + intersection_grid[i].z + " len:" + intersection_grid[i].len + " num:" + intersection_grid[i].num);
            //}
            //OPの計算//////////////////////////////////////////////////////////////////////////////
            for (i = 0; i < temp_len - 1; i++)
            {
                if (intersection_grid[i].num == -1) //データが入っていなければそこで計算終わり
                {
                    break;
                }
                if (i == 0)
                {
                    intersection_grid[i].op = intersection_grid[i].len - length_LMmG;   //LMｍから一番近い分割光路はLMｍの半分の長さ分だけ引く
                    if (intersection_grid[i].op < 0)    //最初のグリッド交点がLMｍの長さより近いとき
                    {
                        intersection_grid[i].op = 0;    //光路0にして
                        i++;
                        intersection_grid[i].op = intersection_grid[i].len - length_LMmG;   //次の交点についてLMｍの長さを引く処理をする
                    }
                }
                else
                {
                    intersection_grid[i].op = intersection_grid[i].len - intersection_grid[i - 1].len;    //OPの計算
                }
            }

            //値をMESH配列に入れる//////////////////////////////////////////////////////////////////
            int p, q;
            MESH[MEASURE_NUMBER, 0] = intersection_grid[i - 1].len;     //長さ(最大光路)入れる
            //座標入れる
            q = 1;
            double temp_r_size = r_size;
            for (p = 0; p < mesh_size; p++)
            {
                if (-pan * degpos >= 0)     //　x>=0のとき
                {
                    MESH[MEASURE_NUMBER, q++] = temp_r_size * Math.Sin(rad_tilt) * Math.Cos(rad_pan) - _xmin;  //-_xminで座標の補正を行う
                    
                }
                else　 //　x<0のとき                  
                {
                    MESH[MEASURE_NUMBER, q++] = -temp_r_size * Math.Sin(rad_tilt) * Math.Cos(rad_pan) - _xmin;  //-_xminで座標の補正を行う
                    //Console.WriteLine("xmin"+_xmin+" x<0" + (-temp_r_size * Math.Sin(rad_tilt) * Math.Cos(rad_pan)));
                }
                
                MESH[MEASURE_NUMBER, q++] = temp_r_size * Math.Sin(rad_tilt) * Math.Sin(rad_pan);       //y座標
                MESH[MEASURE_NUMBER, q++] = zmax - temp_r_size*Math.Cos(rad_tilt);                      //z座標
                temp_r_size += r_size;
                if(temp_r_size > intersection_grid[i - 1].len)
                {
                    break;
                }
            }

            //***opの計算確認***
            //for (i = 0; i < temp_len; i++)      //***
            //{
            //    Console.WriteLine("num:" + i + " x" + intersection_grid[i].x + " y:" + intersection_grid[i].y + " z:" + intersection_grid[i].z + " len:" + intersection_grid[i].len + " num:" + intersection_grid[i].num + " op:" + intersection_grid[i].op);
            //}
            //光路の計算を見るよう
            //try
            //{
            //    // appendをtrueにすると，既存のファイルに追記
            //    //         falseにすると，ファイルを新規作成する
            //    var append = true;
            //    // 出力用のファイルを開く
            //    using (var sw = new StreamWriter(@"C:\Users\SENS\source\repos\Control_PTU\Control_PTU\csv\OPshow2.csv", append))
            //    {
            //        for (i = 0; i < temp_len; i++)
            //        {
            //            sw.Write(intersection_grid[i].x + ",");   //書き込み
            //        }
            //        sw.WriteLine("x");  //改行
            //        for (i = 0; i < temp_len; i++)
            //        {
            //            sw.Write(intersection_grid[i].y + ",");   //書き込み
            //        }
            //        sw.WriteLine("y");  //改行
            //        for (i = 0; i < temp_len; i++)
            //        {
            //            sw.Write(intersection_grid[i].z + ",");   //書き込み
            //        }
            //        sw.WriteLine("z");  //改行
            //    }
            //}
            //catch (Exception err)
            //{
            //    // ファイルを開くのに失敗したときエラーメッセージを表示
            //    Console.WriteLine(err.Message);
            //}

            //値を最終配列に入れる//////////////////////////////////////////////////////////////////
            //Console.WriteLine("MM" + MEASURE_NUMBER);
            for (i = 0; i < temp_len; i++)
            {
                if (intersection_grid[i].num == -1) //データが入っていなければそこで計算終わり
                {
                    break;
                }
                split_OP[MEASURE_NUMBER, intersection_grid[i].num] = intersection_grid[i].op;
            }
            //最終確認
            //Console.WriteLine("split_OP");
            //for (i = 1; i <= cell_size; i++)
            //{
            //    Console.WriteLine("num:"+i+" op:"+split_OP[MEASURE_NUMBER, i]);
            //}

        }
        //END分割光路長の計算*****************************************************************************************


        //ボクセルナンバー取得関係************************************************************************************
        //交点のグリッド座標からボクセルナンバーを取得する関数***
        private static int get_VOXEL_num(double x, double y, double z, double _xmin, double _xmax)
        {
            if (x < _xmin)
            {
                x = _xmin;      //例外処理0付近の話
            }
            if (x > _xmax)
            {
                x = _xmax;      //例外処理0付近の話
            }
            //Console.WriteLine("▼x:" + x + "y:" + y + "z" + z);
            int num;
            int voxel_x = RangetoNum(RoundUp(x, delta));   //RoundDown(x, delta):分解能まで正規化，/deltaでボクセル座標に変換
            int voxel_y = RangetoNum(RoundUp(y, delta));
            int voxel_z = RangetoNum(RoundDown(z, delta)) + 1;
            //Console.WriteLine("▲x:" + voxel_x + "y:" + voxel_y + "z" + voxel_z);
            num = trans_Voxel_num(voxel_x, voxel_y, voxel_z, _xmin);

            return num;
        }

        //ボクセルナンバーを格納する配列
        private static int[,,] voxel_num = new int[Xrange, Yrange, Zrange];
        //ボクセルナンバーをふる関数
        private static void create_VOXEL_num()
        {
            int num = 0;
            int i, j, k;

            for (k = 0; k < Zrange; k++)
            {
                for (j = 0; j < Yrange; j++)
                {
                    for (i = 0; i < Xrange; i++)
                    {
                        //Console.WriteLine("x:"+i+" y:"+j+" z:"+k+" voxel_num:" + num);
                        voxel_num[i, j, k] = num;
                        num++;
                    }
                }
            }
        }
        //ボクセル座標からボクセルナンバーを取得する関数//get_Voxel_num[ボクセル座標（x,y,z）]
        private static int trans_Voxel_num(int x, int y, int z, double _xmin)
        {
            if (x < 0)  //配列に合うように値を補正する
            {
                x = x - RangetoNum(_xmin);
                y = y - 1;
                z = z - 1;
            }
            else
            {
                x = x - RangetoNum(_xmin) - 1;    //ボクセル座標のx座標には0がないので-1して詰める
                y = y - 1;
                z = z - 1;
            }
            //Console.WriteLine("■x:"+x+"y:"+y+"z"+z);
            //Console.WriteLine("voxel_num:" + voxel_num[x, y, z]);
            return voxel_num[x, y, z];
        }
        //ENDボクセルナンバー取得関係*********************************************************************************

        //構造体のソート関数******************************************************************************************
        private static void SORT(INTERSECTION[] inter, int max)
        {
            int i, j;
            INTERSECTION temp;
            for (i = 0; i < max - 1; i++)
            {
                for (j = max - 1; j > i; j--)
                {
                    if (inter[j - 1].len > inter[j].len)
                    {
                        temp = inter[j - 1];
                        inter[j - 1] = inter[j];
                        inter[j] = temp;
                    }
                }
            }
        }
        //END構造体のソート関数***************************************************************************************
    }
}
