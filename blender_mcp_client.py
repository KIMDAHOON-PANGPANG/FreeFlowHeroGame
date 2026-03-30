"""
Blender MCP Client - Compatible with Blender 5.1 (Layered Action API)
Sends commands to Blender via MCP socket server.
"""
import socket
import json
import sys
import time

HOST = 'localhost'
PORT = 9876

def send_command(command, timeout=120):
    """Send a command to the Blender MCP server and return the response."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(timeout)
    try:
        sock.connect((HOST, PORT))
        payload = json.dumps(command).encode('utf-8')
        sock.sendall(payload)
        
        response_data = b''
        while True:
            try:
                chunk = sock.recv(65536)
                if not chunk:
                    break
                response_data += chunk
                try:
                    result = json.loads(response_data.decode('utf-8'))
                    return result
                except json.JSONDecodeError:
                    continue
            except socket.timeout:
                print("Timeout waiting for response")
                break
        
        if response_data:
            return json.loads(response_data.decode('utf-8'))
        return None
    except Exception as e:
        print(f"Connection Error: {e}")
        return None
    finally:
        sock.close()

def execute_blender_code(code, timeout=120):
    """Execute Python code in Blender via MCP."""
    command = {
        "type": "execute_code",
        "params": {
            "code": code
        }
    }
    result = send_command(command, timeout=timeout)
    if result:
        if result.get("status") == "success":
            output = result.get('result', {}).get('result', '')
            print(output)
            return True
        else:
            print(f"ERROR: {result.get('message', 'Unknown error')}")
            return False
    return False


def main():
    print("=" * 60)
    print("Blender 5.1 MCP Client - Knock_A.fbx Root Bone Fix")
    print("=" * 60)
    
    # Step 0: Test connection
    print("\n[Step 0] Testing connection...")
    result = send_command({"type": "get_scene_info"})
    if not result or result.get("status") != "success":
        print("Failed to connect!")
        sys.exit(1)
    print("Connected to Blender MCP server!")
    time.sleep(0.5)

    # Step 1: Clear and Import
    print("\n[Step 1] Clearing scene...")
    execute_blender_code("""
import bpy

if bpy.context.active_object and bpy.context.active_object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

for block in bpy.data.meshes:
    if block.users == 0:
        bpy.data.meshes.remove(block)
for block in bpy.data.armatures:
    if block.users == 0:
        bpy.data.armatures.remove(block)
for block in bpy.data.actions:
    if block.users == 0:
        bpy.data.actions.remove(block)

print("Scene cleared")
""")
    time.sleep(1)
    
    print("\n[Step 1b] Importing Knock_A.fbx...")
    execute_blender_code(r"""
import bpy

fbx_path = r"C:/Users/sk992/FreeFlowHeroGame/Assets/Martial Art Animations Sample/Animations/Knock_A.fbx"
bpy.ops.import_scene.fbx(filepath=fbx_path, use_prepost_rot=False)

print("FBX imported!")
for obj in bpy.context.scene.objects:
    print(f"  {obj.name} ({obj.type})")
""", timeout=60)
    time.sleep(1)

    # Step 2: Identify bones and animation structure using Blender 5.1 API
    print("\n[Step 2] Analyzing animation structure (Blender 5.1 API)...")
    execute_blender_code("""
import bpy

armature = None
for obj in bpy.context.scene.objects:
    if obj.type == 'ARMATURE':
        armature = obj
        break

if not armature:
    print("ERROR: No armature found!")
else:
    print(f"Armature: {armature.name}")
    
    # Root bone
    for bone in armature.data.bones:
        if bone.parent is None:
            print(f"ROOT BONE: {bone.name}")
    
    # Animation data
    if armature.animation_data:
        action = armature.animation_data.action
        slot = armature.animation_data.action_slot
        print(f"Action: {action.name}")
        print(f"Slot: {slot.name if slot else 'None'}")
        print(f"Layers: {len(action.layers)}")
        
        for li, layer in enumerate(action.layers):
            print(f"  Layer[{li}]: {layer.name}, strips={len(layer.strips)}")
            for si, strip in enumerate(layer.strips):
                print(f"    Strip[{si}]: type={strip.type}")
                
                # Get channelbag for this slot
                if slot:
                    cb = strip.channelbag(slot)
                    if cb:
                        print(f"    Channelbag fcurves: {len(cb.fcurves)}")
                        for fi, fc in enumerate(cb.fcurves):
                            if fi < 40:
                                print(f"      [{fi}] {fc.data_path} [{fc.array_index}] keys={len(fc.keyframe_points)}")
                    else:
                        print("    No channelbag for this slot")
    else:
        print("No animation_data on armature")
""")
    time.sleep(1)

    # Step 3: Remove root bone location curves using Blender 5.1 API
    print("\n[Step 3] Removing root bone location curves...")
    execute_blender_code("""
import bpy

armature = None
for obj in bpy.context.scene.objects:
    if obj.type == 'ARMATURE':
        armature = obj
        break

if not armature:
    print("ERROR: No armature found!")
elif not armature.animation_data or not armature.animation_data.action:
    print("ERROR: No action found!")
else:
    action = armature.animation_data.action
    slot = armature.animation_data.action_slot
    
    # Find root bone
    root_bone_name = None
    for bone in armature.data.bones:
        if bone.parent is None:
            root_bone_name = bone.name
            break
    
    print(f"Root bone: {root_bone_name}")
    root_loc_path = f'pose.bones["{root_bone_name}"].location'
    print(f"Target path: {root_loc_path}")
    
    # Navigate: action -> layer -> strip -> channelbag -> fcurves
    removed_count = 0
    for layer in action.layers:
        for strip in layer.strips:
            cb = strip.channelbag(slot)
            if cb:
                # Collect fcurves to remove
                to_remove = []
                for fc in cb.fcurves:
                    if fc.data_path == root_loc_path:
                        to_remove.append(fc)
                        print(f"  Found: {fc.data_path} [{fc.array_index}] ({len(fc.keyframe_points)} keys)")
                
                for fc in to_remove:
                    cb.fcurves.remove(fc)
                    removed_count += 1
                    print(f"  Removed location curve")
    
    if removed_count == 0:
        print("No location curves found with standard path.")
        # Search for any location-related curves
        for layer in action.layers:
            for strip in layer.strips:
                cb = strip.channelbag(slot)
                if cb:
                    for fc in cb.fcurves:
                        if 'location' in fc.data_path.lower():
                            print(f"  Found alternative: {fc.data_path} [{fc.array_index}]")
    else:
        print(f"Removed {removed_count} location curves")
    
    # Verify rotation curves intact
    rot_count = 0
    for layer in action.layers:
        for strip in layer.strips:
            cb = strip.channelbag(slot)
            if cb:
                for fc in cb.fcurves:
                    if root_bone_name in fc.data_path and 'rotation' in fc.data_path:
                        rot_count += 1
    print(f"Root rotation curves preserved: {rot_count}")
    
    # Count remaining total
    total = 0
    for layer in action.layers:
        for strip in layer.strips:
            cb = strip.channelbag(slot)
            if cb:
                total += len(cb.fcurves)
    print(f"Total remaining fcurves: {total}")
""")
    time.sleep(1)

    # Step 4: Set root bone location to 0,0,0
    print("\n[Step 4] Setting root bone to (0,0,0)...")
    execute_blender_code("""
import bpy

armature = None
for obj in bpy.context.scene.objects:
    if obj.type == 'ARMATURE':
        armature = obj
        break

if armature:
    bpy.context.view_layer.objects.active = armature
    if bpy.context.active_object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.mode_set(mode='POSE')
    
    root_bone_name = None
    for bone in armature.data.bones:
        if bone.parent is None:
            root_bone_name = bone.name
            break
    
    pose_bone = armature.pose.bones[root_bone_name]
    action = armature.animation_data.action
    slot = armature.animation_data.action_slot
    
    if action:
        # Get frame range
        frame_start = int(action.frame_range[0])
        frame_end = int(action.frame_range[1])
        
        # Set location to 0 and insert keyframes
        for frame in [frame_start, frame_end]:
            bpy.context.scene.frame_set(frame)
            pose_bone.location = (0, 0, 0)
            pose_bone.keyframe_insert(data_path="location", frame=frame)
        
        # Zero out all location keyframe values
        root_loc_path = f'pose.bones["{root_bone_name}"].location'
        for layer in action.layers:
            for strip in layer.strips:
                cb = strip.channelbag(slot)
                if cb:
                    for fc in cb.fcurves:
                        if fc.data_path == root_loc_path:
                            for kp in fc.keyframe_points:
                                kp.co[1] = 0.0
                                kp.handle_left[1] = 0.0
                                kp.handle_right[1] = 0.0
                            fc.update()
        
        print(f"Root bone '{root_bone_name}' locked at (0,0,0)")
        print(f"Keyframes at frames {frame_start} and {frame_end}")
    else:
        pose_bone.location = (0, 0, 0)
        print(f"Root bone '{root_bone_name}' set to (0,0,0)")
    
    bpy.ops.object.mode_set(mode='OBJECT')
    print("Done")
""")
    time.sleep(1)

    # Step 5: Backup and Export
    print("\n[Step 5] Exporting FBX...")
    execute_blender_code(r"""
import bpy
import shutil
import os

fbx_path = r"C:/Users/sk992/FreeFlowHeroGame/Assets/Martial Art Animations Sample/Animations/Knock_A.fbx"
backup_path = fbx_path.replace('.fbx', '_original.fbx')

if not os.path.exists(backup_path):
    shutil.copy2(fbx_path, backup_path)
    print(f"Backup: {os.path.basename(backup_path)}")
else:
    print(f"Backup exists: {os.path.basename(backup_path)}")

if bpy.context.active_object and bpy.context.active_object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

bpy.ops.object.select_all(action='DESELECT')
for obj in bpy.context.scene.objects:
    obj.select_set(True)
    if obj.type == 'ARMATURE':
        bpy.context.view_layer.objects.active = obj

bpy.ops.export_scene.fbx(
    filepath=fbx_path,
    use_selection=True,
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_UNITS',
    axis_forward='-Z',
    axis_up='Y',
    bake_anim=True,
    bake_anim_simplify_factor=0.0,
    use_armature_deform_only=False,
    add_leaf_bones=False,
    bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True,
)

print(f"Exported: {os.path.basename(fbx_path)}")
print("COMPLETE!")
""", timeout=60)
    
    print("\n" + "=" * 60)
    print("ALL DONE!")
    print("- Knock_A.fbx: 루트 본(Hips) 위치 커브 제거 & 0,0,0 고정")
    print("- Knock_A_original.fbx: 원본 백업 보존")
    print("- Unity에서 Reimport 하세요!")
    print("=" * 60)


if __name__ == "__main__":
    main()
