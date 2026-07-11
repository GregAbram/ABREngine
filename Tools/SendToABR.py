from paraview.util.vtkAlgorithm import *
import os, sys, json

@smproxy.filter()

@smproperty.input(name="InputDataset", port_index=0)
@smdomain.datatype(dataTypes=["vtkDataSet"], composite_data_supported=False)

class Send2ABR(VTKPythonAlgorithmBase):
    def __init__(self):
        VTKPythonAlgorithmBase.__init__(self, nInputPorts=1, nOutputPorts=1, outputType="vtkUnstructuredGrid")
        self.institution = 'GDA' # 'local'
        self.project = 'test' # 'test'
        self.keydata = 'key' # 'keydata'
        self.host  = 'localhost'
        self.port  = 1900
        self.logfile = ""

    def FillInputPortInformation(self, port, info):
        info.Set(self.INPUT_REQUIRED_DATA_TYPE(), "vtkDataSet")
        return 1

    # @smproperty.stringvector(name="Host", default_values="localhost")
    @smproperty.stringvector(name="Host")
    def SetHost(self, value):
        self.host = value
        self.Modified()
        return

    @smproperty.intvector(name="Port", default_values=1900)
    def SetPort(self, value):
        self.port = value
        self.Modified()

    @smproperty.stringvector(name="Institution", default_values="GDA")
    def SetInstitution(self, value):
        self.institution = value
        self.Modified()
        return

    @smproperty.stringvector(name="Project", default_values="test")
    def SetProject(self, value):
        self.project = value
        self.Modified()
        return

    @smproperty.stringvector(name="KeyData", default_values="key")
    def SetKeyData(self, value):
        self.keydata = value
        self.Modified()
        return

    def RequestData(self, request, inInfoVec, outInfoVec):
        from paraview import servermanager as sm
        from paraview.simple import GetActiveView
        import sys, os
        import json
        import struct
        import socket
        import select
        from enum import Enum
        import numpy as np
        from vtk.numpy_interface import dataset_adapter as dsa
        import vtk

        outpt = vtk.vtkUnstructuredGrid.GetData(outInfoVec, 0)
        ug = vtk.vtkUnstructuredGrid.GetData(inInfoVec[0], 0)

        if ug == None:
          pd = vtk.vtkPolyData.GetData(inInfoVec[0], 0)
          if pd != None:
            af = vtk.vtkAppendFilter()
            af.SetInputData(pd)
            af.Update()
            ug = af.GetOutput()
            del af
          else:
            print("Can only handle PolyData and UnstructuredGrids")
            return 0

        outpt.ShallowCopy(ug)

        def WaitForAck(socket):
            print("Wait for ack")
            ack = b'none'
            knt = 0
            r = -1
            while ack != b'ok' and knt < 5:
                knt = knt + 1
                try:
                    r,w,e = select.select([socket], [], [socket], 5)
                    if len(r) > 0:
                        bytes = socket.recv(4)
                        sz = int.from_bytes(bytes, byteorder='big')
                        ack = socket.recv(2)
                    else:
                        print('timeout')
                except OSError as error:
                    print(error)
                    r = 0
            if ack == b'ok':
              r = 1
            elif knt >= 5:
              print("loop error")
              r = 0
              r = 0
            return r

        if 1 == 1 or 'UnitySyncer' not in dir(sm):
          print("installing syncer")
          def callback(caller, *args):
            print('callback')
            from struct import pack
            from paraview import servermanager as sm
            if 1 == 1 or 'UnityModified' not in dir(sm) or sm.UnityModified == 1:
              # print("callback update :  mod = ", sm.UnityModified)
              sm.UnityModified = 0
              import socket
              sm.UnityFrame = sm.UnityFrame + 1
              s = socket.socket()
              try:
                print("tring to connect: %s %d" % (self.host, self.port))
                s.connect((self.host, self.port))
              except:
                print('USyncer unable to connect')
                return
              print("connect OK! keeping going")
              update = 'update'.encode()
              s.send(pack('!i', len(update)))
              s.send(update)
              print("update msg sent")
              ack = b'none'
              knt = 0
              while ack != b'ok' and knt < 5:
                print("Loop 1")
                knt = knt + 1
                try:
                  print("Entering select")
                  r,w,e = select.select([s], [], [s], 5)
                  print("Exiting select")
                  if len(r) > 0:
                      bytes = s.recv(4)
                      sz = int.from_bytes(bytes, byteorder='big')
                      ack = s.recv(2)
                      print("got ", ack)
                  else:
                      print("timeout")
                except OSError as error:
                  print(error)
                  r = 0
              if ack == b'ok':
                print('got update ack')
              elif knt >= 5:
                print('failed to get update ack')
              s.close()
              sm.UnityModified = 0
          try:
            print("Installing syncer - Active View is ", GetActiveView())
            sm.UnitySyncer = GetActiveView().AddObserver('EndEvent', callback, 1.0)
            sm.UnityFrame  = 0
          except:
            print("SendToABR error installing update manager")
        else:
            print("SendToABR unity syncer already installed")

        class UnityMeshTopology(int, Enum):
          Triangles = 0,
          Quads = 2,
          Lines = 3,
          LineStrip = 4,
          Points = 5,

        VTK_TO_TOPOLOGY = {
          # Point data
          vtk.VTK_VERTEX: UnityMeshTopology.Points,
          vtk.VTK_POLY_VERTEX: UnityMeshTopology.Points,

          # Line data
          vtk.VTK_POLY_LINE: UnityMeshTopology.LineStrip,
          vtk.VTK_LINE: UnityMeshTopology.Lines,

          # Surface data
          vtk.VTK_QUAD: UnityMeshTopology.Quads,
          vtk.VTK_TRIANGLE: UnityMeshTopology.Triangles,
        }

        num_points = ug.GetNumberOfPoints()
        num_cells = ug.GetNumberOfCells()

        try:
          print("trying to connect to %s at port %d" % (self.host, self.port))
          s = socket.socket(socket.AF_INET, socket.SOCK_STREAM, 0)
          s.connect((self.host, self.port))
          print("connected")
        except:
          print('connection failed')
          return 1

        if ug.GetNumberOfCells() > 0:

          first_cell_type = ug.GetCell(0).GetCellType()
          topology = VTK_TO_TOPOLOGY[first_cell_type]

          np_dataset = dsa.WrapDataObject(ug)

          scalar_arrays = []
          vector_arrays = []

          scalar_mins = []
          scalar_maxes = []
          scalar_array_names = []
          vector_array_names = []

          point_data = np_dataset.PointData

          # quietly ignore any arrays that are not scalar or 3-vector

          for name, arr in zip(point_data.keys(), point_data):
            if len(arr.shape) == 1 or arr.shape[1] == 1:
              arr = np.nan_to_num(arr).astype('f4')
              scalar_array_names.append(name)
              scalar_arrays.append(arr)
              scalar_mins.append(float(np.amin(arr)))
              scalar_maxes.append(float(np.max(arr)))
            elif len(arr.shape) == 2 and arr.shape[1] == 3:
              arr = np.nan_to_num(arr).astype('f4')
              vector_arrays.append(arr)
              vector_array_names.append(name)

          # Flip the z component of vector assuming it's a 3-vec. This also is based on
          # the assumption that a 3-vec represents something spatial, and that Paraview
          # is right-handed and Unity is left-handed.   Also convert NANs and create
          # list of dicts

          vertex_array = np.nan_to_num(np_dataset.Points * [1, 1, -1]).astype('f4')

          if (topology == UnityMeshTopology.Lines) or (topology == UnityMeshTopology.Triangles)or (topology == UnityMeshTopology.Quads) or (topology == UnityMeshTopology.LineStrip):
            k = 1
          else:
            k = 0

          if k == 0:
            cells = np.column_stack(([1]*np_dataset.GetNumberOfPoints(), np.arange(np_dataset.GetNumberOfPoints()))).flatten()
            num_cells = np_dataset.GetNumberOfPoints()
          else:
            cells = np_dataset.Cells

          cells = cells.astype('i4')

          b = np.array(np_dataset.VTKObject.GetBounds())
          c = ((b[[1,3,5]] + b[[0,2,4]]) / 2.0).tolist()
          e = ((b[[1,3,5]] - b[[0,2,4]]) / 2.0).tolist()
          bounds = {'m_Center': {'x': c[0], 'y': c[1], 'z': -c[2]}, 'm_Extent': {'x': e[0], 'y': e[1], 'z': e[2]}}

          data = {
            'meshTopology': int(topology),
            'num_points': num_points,
            'num_cells': num_cells,
            'num_cell_indices': cells.size,
            'scalarArrayNames': scalar_array_names,
            'vectorArrayNames': vector_array_names,
            'bounds': bounds,
            'scalarMaxes': scalar_maxes,
            'scalarMins': scalar_mins
          }

          stringified_json = str.encode(json.dumps(data))
          len_stringified_json = len(stringified_json)

          # Get total size of data block

          # space for points
          bufsize = 4*(3*num_points)

          # add space for point-dep variables
          bufsize = bufsize + 4*((len(scalar_array_names) + 3*len(vector_array_names)) * num_points)

          # add space for indices
          bufsize = bufsize + 4*cells.size

          if s != None:

            def snd(skt, bytes):
              offset = 0
              knt = len(bytes)
              while knt > 0:
                n = skt.send(bytes[offset:])
                knt = knt - n
                offset = offset + n

            label = self.institution + '/' + self.project + '/KeyData/' + self.keydata
            print('sending to ', label)
            etag = 'keydata'.encode()
            elabel = label.encode()

            s.send(struct.pack('>i', len(etag)))
            s.send(etag)

            s.send(struct.pack('>i', len(elabel)))
            s.send(elabel)

            s.send(struct.pack('>I', len_stringified_json))
            snd(s, stringified_json)

            s.send(struct.pack('>I', bufsize))

            snd(s, vertex_array.tobytes())

            snd(s, cells.tobytes())

            for i in range(len(scalar_array_names)):
              snd(s, scalar_arrays[i].tobytes())

            for i in range(len(vector_array_names)):
              snd(s, vector_arrays[i].tobytes())

            ack = WaitForAck(s)
            if ack == 0:
                print('ack for label %s failed' % label)
            else:
                print('received ack for label %s' % label)

            s.close()
            sm.UnityModified = 1

          else:
            print("no connection ... no send")

        return 1


@smproxy.filter()

@smproperty.input(name="InputDataset", port_index=0)
@smdomain.datatype(dataTypes=["vtkDataSet"], composite_data_supported=False)

class Send2ABRProjectInfo(VTKPythonAlgorithmBase):
    """Sends the bounding box of the ORIGINAL, top-level dataset (as opposed
    to any one filter/selection derived from it, e.g. a contour or slice) to
    ABREngine. ABREngine saves this as project.json alongside that project's
    key data .json/.bin files, and uses it as the authoritative data-space
    extent that all of a project's key data get squished into together -
    since individual key data (a contour, a slice) may each only cover part
    of the original dataset's extent, and must not be normalized
    independently of one another.
    """

    def __init__(self):
        VTKPythonAlgorithmBase.__init__(self, nInputPorts=1, nOutputPorts=1, outputType="vtkUnstructuredGrid")
        self.institution = 'GDA'
        self.project = 'test'
        self.host = 'localhost'
        self.port = 1900

    def FillInputPortInformation(self, port, info):
        info.Set(self.INPUT_REQUIRED_DATA_TYPE(), "vtkDataSet")
        return 1

    @smproperty.stringvector(name="Host")
    def SetHost(self, value):
        self.host = value
        self.Modified()

    @smproperty.intvector(name="Port", default_values=1900)
    def SetPort(self, value):
        self.port = value
        self.Modified()

    @smproperty.stringvector(name="Institution", default_values="GDA")
    def SetInstitution(self, value):
        self.institution = value
        self.Modified()

    @smproperty.stringvector(name="Project", default_values="test")
    def SetProject(self, value):
        self.project = value
        self.Modified()

    def RequestData(self, request, inInfoVec, outInfoVec):
        import struct
        import socket
        import select
        import numpy as np
        import vtk

        outpt = vtk.vtkUnstructuredGrid.GetData(outInfoVec, 0)
        ug = vtk.vtkUnstructuredGrid.GetData(inInfoVec[0], 0)

        if ug == None:
            pd = vtk.vtkPolyData.GetData(inInfoVec[0], 0)
            if pd != None:
                af = vtk.vtkAppendFilter()
                af.SetInputData(pd)
                af.Update()
                ug = af.GetOutput()
                del af
            else:
                print("Can only handle PolyData and UnstructuredGrids")
                return 0

        outpt.ShallowCopy(ug)

        def WaitForAck(skt):
            ack = b'none'
            knt = 0
            r = -1
            while ack != b'ok' and knt < 5:
                knt = knt + 1
                try:
                    r, w, e = select.select([skt], [], [skt], 5)
                    if len(r) > 0:
                        sz = skt.recv(4)
                        ack = skt.recv(2)
                    else:
                        print('timeout')
                except OSError as error:
                    print(error)
                    r = 0
            return 1 if ack == b'ok' else 0

        b = np.array(ug.GetBounds())
        c = ((b[[1, 3, 5]] + b[[0, 2, 4]]) / 2.0).tolist()
        e = ((b[[1, 3, 5]] - b[[0, 2, 4]]) / 2.0).tolist()
        bounds = {'m_Center': {'x': c[0], 'y': c[1], 'z': -c[2]}, 'm_Extent': {'x': e[0], 'y': e[1], 'z': e[2]}}

        data = {'bounds': bounds}
        stringified_json = str.encode(json.dumps(data))

        label = self.institution + '/' + self.project
        print('sending project info to ', label)

        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM, 0)
            s.connect((self.host, self.port))
        except:
            print('connection failed')
            return 1

        etag = 'projectinfo'.encode()
        elabel = label.encode()

        s.send(struct.pack('>i', len(etag)))
        s.send(etag)

        s.send(struct.pack('>i', len(elabel)))
        s.send(elabel)

        s.send(struct.pack('>I', len(stringified_json)))
        s.send(stringified_json)

        # No binary payload for a project info message
        s.send(struct.pack('>I', 0))

        ack = WaitForAck(s)
        if ack == 0:
            print('ack for project info %s failed' % label)
        else:
            print('received ack for project info %s' % label)

        s.close()

        return 1
