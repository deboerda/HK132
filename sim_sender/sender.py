# /// script
# requires-python = ">=3.9"
# dependencies = [
#     "pandas",
#     "protobuf",
# ]
# ///
import pandas as pd
import time
import socket
import base64
import sys
import os

# 导入protoc生成的pb文件
import ddm_pb2

CSV_PATH = r"c:\unityproject\unity project\HK132\Assets\data\hk132_clean.csv"
UDP_IP = "127.0.0.1"
UDP_PORT = 32701
FREQ_HZ = 10.0  # 发送频率 5Hz (0.2s/次)

def main():
    if not os.path.exists(CSV_PATH):
        print(f"Error: 找不到CSV文件: {CSV_PATH}")
        sys.exit(1)

    print(f"正在加载CSV文件 {CSV_PATH}...")
    df = pd.read_csv(CSV_PATH)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    
    col_names = df.columns.tolist()
    is_numeric = []
    
    for col in col_names:
        if pd.api.types.is_numeric_dtype(df[col]):
            is_numeric.append(True)
        else:
            is_numeric.append(False)
            
    num_rows = len(df)
    print(f"加载了 {num_rows} 行数据。")

    # 基于最后两行计算联想（线性外推）增量
    extrapolate_deltas = {}
    last_row_data = {}
    
    if num_rows >= 2:
        last_row = df.iloc[num_rows - 1]
        prev_row = df.iloc[num_rows - 2]
        
        for i, col in enumerate(col_names):
            last_row_data[col] = last_row[col]
            if is_numeric[i]:
                # 简单线性外推增量
                extrapolate_deltas[col] = float(last_row[col]) - float(prev_row[col])
            else:
                extrapolate_deltas[col] = 0

    current_row_idx = 0
    sim_time = 0.0
    dt = 1.0 / FREQ_HZ

    print(f"开始传输 (目标: {UDP_IP}:{UDP_PORT}, 频率: {FREQ_HZ}Hz)...")
    try:
        while True:
            ddm_data = ddm_pb2.DDMData()
            ddm_data.machine_time = time.time()
            ddm_data.sim_time = sim_time
            ddm_data.is_start_sim = True
            
            # 映射表：将 CSV 表头映射为 Unity UDPDataReceiver.cs 期望的短名称 (0_longitude, 0_latitude, etc.)
            mapping = {
                "[载机位置数据][_经度]": "0_longitude",
                "[载机位置数据][_纬度]": "0_latitude",
                "[载机惯性气压高度][_高度]": "0_altitude"
            }

            # 添加一个通用的 SimTime 变量供 Receiver 使用
            st = ddm_data.sim_vars.add()
            st.name = "SimTime"
            st.type = ddm_pb2.DDMData.SimVar.Type.Double
            st.double_value = sim_time

            # 使用 CSV 文件中的数据填充 sim_vars
            for i, col in enumerate(col_names):
                sv = ddm_data.sim_vars.add()
                # 如果在映射表中，则使用映射后的名称，否则使用原始名称
                sv.name = mapping.get(col, str(col))
                
                if current_row_idx < num_rows:
                    val = df.iloc[current_row_idx][col]
                else:
                    # 超过CSV已有数据后，使用外推联想后续数据配合仿真
                    if is_numeric[i]:
                        last_row_data[col] += extrapolate_deltas[col]
                        val = last_row_data[col]
                    else:
                        val = last_row_data[col] # 字符串维持最后状态
                        
                if is_numeric[i]:
                    sv.type = ddm_pb2.DDMData.SimVar.Type.Double
                    sv.double_value = float(val) if not pd.isna(val) else 0.0
                else:
                    sv.type = ddm_pb2.DDMData.SimVar.Type.String
                    sv.string_value = str(val)

            # 序列化 Protobuf
            pb_bytes = ddm_data.SerializeToString()
            # 转换为 Base64 字符串发送，配合 Unity C# 的 ASCII 编码读取
            b64_bytes = base64.b64encode(pb_bytes)
            
            sock.sendto(b64_bytes, (UDP_IP, UDP_PORT))
            
            if current_row_idx % int(FREQ_HZ) == 0:
                print(f"已发送数据行 -> 仿真时间: {sim_time:.2f}s, 当前索引: {current_row_idx}, 变量数: {len(ddm_data.sim_vars)}" + (" (联想预测阶段)" if current_row_idx >= num_rows else ""))

            current_row_idx += 1
            sim_time += dt
            
            # 简单的频率控制
            time.sleep(dt)

    except KeyboardInterrupt:
        print(" 用户终止传输。")
    finally:
        sock.close()
        
if __name__ == "__main__":
    main()
