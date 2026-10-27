import { Badge } from "@/components/ui/badge";
import { PHASE_LABEL, campaignPhase } from "@/lib/campaign-time";

/** Agendada, no ar ou encerrada, pelo relógio. */
export function CampaignPhase({ campaign }: { campaign: { starts_at: string; ends_at: string } }) {
  const phase = campaignPhase(campaign);
  return (
    <Badge variant={phase === "running" ? "success" : phase === "scheduled" ? "neutral" : "outline"}>
      {PHASE_LABEL[phase]}
    </Badge>
  );
}
