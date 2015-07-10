using UnityEngine;
using UnityEditor;

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using MiniJSONForAssetGraph;
 

namespace AssetGraph {
	public class AssetGraph : EditorWindow {
		[MenuItem(AssetGraphSettings.GUI_TEXT_MENU_OPEN)]
		public static void Open() {
			var window = GetWindow<AssetGraph>();
			window.InitializeGraph();
		}

		List<Node> nodes = new List<Node>();
		List<Connection> connections = new List<Connection>();

		private OnNodeEvent currentEventSource;

		public ConnectionPoint modifingConnnectionPoint;

		public enum ModifyMode : int {
			CONNECT_STARTED,
			CONNECT_ENDED,
		}
		private ModifyMode modifyMode;

		private DateTime lastLoaded = DateTime.MinValue;

		/**
			node window initializer.
			setup nodes, points and connections from saved data.
		*/
		public void InitializeGraph () {
			var basePath = Path.Combine(Application.dataPath, AssetGraphSettings.ASSETGRAPH_TEMP_PATH);
			
			// create Temp folder under Assets/AssetGraph
			if (!Directory.Exists(basePath)) Directory.CreateDirectory(basePath);

			var graphDataPath = Path.Combine(basePath, AssetGraphSettings.ASSETGRAPH_DATA_NAME);


			var deserialized = new Dictionary<string, object>();
			var lastModified = DateTime.Now;

			if (File.Exists(graphDataPath)) {

				Debug.LogError("起動時、表示前にこのへんでコンパイル結果のSetupを実行して、jsonの更新を行う。");

				// load
				var dataStr = string.Empty;
				
				using (var sr = new StreamReader(graphDataPath)) {
					dataStr = sr.ReadToEnd();
				}

				deserialized = Json.Deserialize(dataStr) as Dictionary<string, object>;
				var lastModifiedStr = deserialized[AssetGraphSettings.ASSETGRAPH_DATA_LASTMODIFIED] as string;
				lastModified = Convert.ToDateTime(lastModifiedStr);
			} else {
				// renew
				var graphData = new Dictionary<string, object>{
					{AssetGraphSettings.ASSETGRAPH_DATA_LASTMODIFIED, lastModified.ToString()},
					{AssetGraphSettings.ASSETGRAPH_DATA_NODES, new List<Node>()},
					{AssetGraphSettings.ASSETGRAPH_DATA_CONNECTIONS, new List<Connection>()}
				};

				// save new empty graph data.
				UpdateGraphData(graphData);
			}

			/*
				do nothing if json does not modified after load.
			*/
			if (lastModified == lastLoaded) {
				return;
			}



			lastLoaded = lastModified;

			ResetGUI();


			minSize = new Vector2(600f, 300f);
			
			wantsMouseMove = true;
			modifyMode = ModifyMode.CONNECT_ENDED;
			

			var nodesSource = deserialized[AssetGraphSettings.ASSETGRAPH_DATA_NODES] as List<object>;

			foreach (var nodeDictSource in nodesSource) {
				var nodeDict = nodeDictSource as Dictionary<string, object>;
				var name = nodeDict[AssetGraphSettings.NODE_CLASSNAME] as string;
				var id = nodeDict[AssetGraphSettings.NODE_ID] as string;
				var kindSource = nodeDict[AssetGraphSettings.NODE_KIND] as string;

				var kind = AssetGraphSettings.NodeKindFromString(kindSource);

				switch (kind) {
					case AssetGraphSettings.NodeKind.SOURCE: {
						Debug.LogError("Source定義を特殊なノードとして読み込む必要がある");
						break;
					}
					case AssetGraphSettings.NodeKind.FILTER:
					case AssetGraphSettings.NodeKind.PREFABRICATOR:
					case AssetGraphSettings.NodeKind.BUNDLIZER: {
						var scriptPath = nodeDict[AssetGraphSettings.NODE_SCRIPT_PATH] as string;

						var posDict = nodeDict[AssetGraphSettings.NODE_POS] as Dictionary<string, object>;
						var x = (float)Convert.ToInt32(posDict[AssetGraphSettings.NODE_POS_X]);
						var y = (float)Convert.ToInt32(posDict[AssetGraphSettings.NODE_POS_Y]);
						
						var newNode = new Node(EmitEvent, nodes.Count, name, id, kind, scriptPath, x, y);

						var outputLabelsList = nodeDict[AssetGraphSettings.NODE_OUTPUT_LABELS] as List<object>;
						foreach (var outputLabelSource in outputLabelsList) {
							var label = outputLabelSource as string;
							newNode.AddConnectionPoint(new OutputPoint(label));
						}

						nodes.Add(newNode);
						break;
					}
					case AssetGraphSettings.NodeKind.DESTINATION: {
						Debug.LogError("Destination定義を特殊なノードとして読み込む必要がある");
						break;
					}
				}
			}


			// add default input if node is not NodeKind.SOURCE.
			foreach (var node in nodes) {
				if (node.kind == AssetGraphSettings.NodeKind.SOURCE) continue;
				node.AddConnectionPoint(new InputPoint(AssetGraphSettings.DEFAULT_INPUTPOINT_LABEL));
			}

			Debug.LogError("ラベルとidの配置を修正、やはり別物として扱う。ラベルにユニークを求めるのと、接続性のidにユニークを求めるのは間違っていたと思うので。");
			// load connections
			var connectionsSource = deserialized[AssetGraphSettings.ASSETGRAPH_DATA_CONNECTIONS] as List<object>;
			foreach (var connectionSource in connectionsSource) {
				var connectionDict = connectionSource as Dictionary<string, object>;
				var label = connectionDict[AssetGraphSettings.CONNECTION_LABEL] as string;
				var fromNodeId = connectionDict[AssetGraphSettings.CONNECTION_FROMNODE] as string;
				var toNodeId = connectionDict[AssetGraphSettings.CONNECTION_TONODE] as string;

				var startNode = nodes.Where(node => node.id == fromNodeId).ToList()[0];
				var startPoint = startNode.ConnectionPointFromLabel(label);
				var endNode = nodes.Where(node => node.id == toNodeId).ToList()[0];
				var endPoint = endNode.ConnectionPointFromLabel(AssetGraphSettings.DEFAULT_INPUTPOINT_LABEL);

				AddConnection(label, startNode, startPoint, endNode, endPoint);
			}
		}

		private void ResetGUI () {
			nodes = new List<Node>();
			connections = new List<Connection>();
		}

		void OnGUI () {
			// update node window x N
			{
				BeginWindows();
				
				nodes.ForEach(node => node.UpdateNodeRect());

				EndWindows();
			}

			connections.ForEach(con => con.DrawConnection());

			/*
				draw line if modifing connection.
			*/
			switch (modifyMode) {
				case ModifyMode.CONNECT_STARTED: {
					// from start node to mouse.
					DrawStraightLineFromCurrentEventSourcePointTo(Event.current.mousePosition);
					break;
				}
				case ModifyMode.CONNECT_ENDED: {
					// do nothing
					break;
				}
			}
		}

		private void DrawStraightLineFromCurrentEventSourcePointTo (Vector2 to) {
			if (currentEventSource == null) return;
			var p = currentEventSource.eventSourceNode.GlobalConnectionPointPosition(currentEventSource.eventSourceConnectionPoint);
			Handles.DrawLine(new Vector3(p.x, p.y, 0f), new Vector3(to.x, to.y, 0f));
		}

		private void UpdateGraphData (Dictionary<string, object> data) {
			var dataStr = Json.Serialize(data);

			var basePath = Path.Combine(Application.dataPath, AssetGraphSettings.ASSETGRAPH_TEMP_PATH);
			var graphDataPath = Path.Combine(basePath, AssetGraphSettings.ASSETGRAPH_DATA_NAME);
			using (var sw = new StreamWriter(graphDataPath)) {
				sw.Write(dataStr);
			}
		}

		/**
			emit event from node-GUI.
		*/
		public void EmitEvent (OnNodeEvent e) {
			switch (modifyMode) {
				case ModifyMode.CONNECT_STARTED: {
					switch (e.eventType) {
						/*
							handling
						*/
						case OnNodeEvent.EventType.EVENT_CONNECTIONPOINT_HANDLING: {

							/*
								animate connectionPoint under mouse if this connectionPoint is able to accept this kind of connection.
							*/
							if (false) {
								var candidateNodes = NodesUnderPosition(e.globalMousePosition);

								if (!candidateNodes.Any()) break;
								var nodeUnderMouse = candidateNodes.Last();

								// ignore if target node is source itself.
								if (nodeUnderMouse == e.eventSourceNode) break;
								
								var candidatePoints = nodeUnderMouse.ConnectionPointUnderGlobalPos(e.globalMousePosition);

								var sourcePoint = currentEventSource.eventSourceConnectionPoint;

								// limit by connectable or not.
								var connectableCandidates = candidatePoints.Where(point => IsConnectablePointFromTo(sourcePoint, point)).ToList();
								if (!connectableCandidates.Any()) break;

								// connectable point is exist. change line color. 

								// or, do something..
								Debug.Log("connectable!");
							}
							break;
						}

						/*
							drop detected.
						*/
						case OnNodeEvent.EventType.EVENT_CONNECTIONPOINT_DROPPED: {
							// finish connecting mode.
							modifyMode = ModifyMode.CONNECT_ENDED;
							
							/*
								connect when dropped target is connectable from start connectionPoint.
							*/
							{
								var candidateNodes = NodesUnderPosition(e.globalMousePosition);

								if (!candidateNodes.Any()) break;
								var nodeUnderMouse = candidateNodes.Last();

								// ignore if target node is source itself.
								if (nodeUnderMouse == e.eventSourceNode) break;
								
								var candidatePoints = nodeUnderMouse.ConnectionPointUnderGlobalPos(e.globalMousePosition);

								var sourcePoint = currentEventSource.eventSourceConnectionPoint;

								// limit by connectable or not.
								var connectableCandidates = candidatePoints.Where(point => IsConnectablePointFromTo(sourcePoint, point)).ToList();
								if (!connectableCandidates.Any()) break;


								// target point is determined.
								var connectablePoint = connectableCandidates.First();
								
								var startNode = e.eventSourceNode;
								var startConnectionPoint = currentEventSource.eventSourceConnectionPoint;
								var endNode = nodeUnderMouse;
								var endConnectionPoint = connectablePoint;

								// reverse if connected from input to output.
								if (startConnectionPoint.isInput) {
									startNode = nodeUnderMouse;
									startConnectionPoint = connectablePoint;
									endNode = e.eventSourceNode;
									endConnectionPoint = currentEventSource.eventSourceConnectionPoint;
								}

								var label = startConnectionPoint.label;
								AddConnection(label, startNode, startConnectionPoint, endNode, endConnectionPoint);
							}
							break;
						}

						default: {
							// Debug.Log("unconsumed or ignored event:" + e.eventType);
							modifyMode = ModifyMode.CONNECT_ENDED;
							break;
						}
					}
					break;
				}
				case ModifyMode.CONNECT_ENDED: {
					switch (e.eventType) {
						/*
							start connection handling.
						*/
						case OnNodeEvent.EventType.EVENT_CONNECTIONPOINT_HANDLE_STARTED: {
							modifyMode = ModifyMode.CONNECT_STARTED;
							currentEventSource = e;
							break;
						}

						/*
							connectionPoint tapped.
						*/
						case OnNodeEvent.EventType.EVENT_CONNECTIONPOINT_RECEIVE_TAPPED: {
							var sourcePoint = e.eventSourceConnectionPoint;

							var relatedConnections = connections.
								Where(
									con => con.IsStartAtConnectionPoint(sourcePoint) || 
									con.IsEndAtConnectionPoint(sourcePoint)
								).
								ToList();

							/*
								show menuContext for control these connections.
							*/
							var menu = new GenericMenu();
							foreach (var con in relatedConnections) {
								var message = string.Empty;
								if (sourcePoint.isInput) message = "from " + con.startPointInfo;
								if (sourcePoint.isOutput) message = "to " + con.endPointInfo;
								
								var conId = con.connectionId;

								menu.AddItem(
									new GUIContent("delete connection:" + con.label + " " + message), 
									false, 
									() => DeleteConnectionById(conId)
								);
							}
							menu.ShowAsContext();
							break;
						}

						default: {
							// Debug.Log("unconsumed or ignored event:" + e.eventType);
							break;
						}
					}
					break;
				}
			}
		}

		/**
			create new connection if same relationship is not exist yet.
		*/
		private void AddConnection (string label, Node startNode, ConnectionPoint startPoint, Node endNode, ConnectionPoint endPoint) {
			if (!connections.ContainsConnection(startNode, startPoint, endNode, endPoint)) {
				connections.Add(new Connection(label, startNode, startPoint, endNode, endPoint));
			}
		}

		private List<Node> NodesUnderPosition (Vector2 pos) {
			return nodes.Where(n => n.ConitainsGlobalPos(pos)).ToList();
		}

		private bool IsConnectablePointFromTo (ConnectionPoint sourcePoint, ConnectionPoint destPoint) {
			if (sourcePoint.isOutput != destPoint.isOutput && sourcePoint.isInput != destPoint.isInput) {
				return true;
			}
			return false;
		}

		private void DeleteConnectionByRelation (Node startNode, ConnectionPoint startPoint, Node endNode, ConnectionPoint endPoint) {
			connections.Where(con => con.IsSameDetail(startNode, startPoint, endNode, endPoint)).
				Select(con => connections.Remove(con));
		}

		private void DeleteConnectionById (string connectionId) {
			for (var i = 0; i < connections.Count; i++) {
				var con = connections[i];
				if (con.connectionId == connectionId) connections.Remove(con);
			}
		}
	}
}