{{- define "svc.name" -}}{{ .Release.Name }}{{- end -}}

{{- define "svc.labels" -}}
app.kubernetes.io/name: {{ include "svc.name" . }}
app.kubernetes.io/part-of: compra-facil
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
{{- end -}}

{{- define "svc.selector" -}}
app.kubernetes.io/name: {{ include "svc.name" . }}
{{- end -}}
